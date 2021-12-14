# IncrementalGenerator

Source generators were added as a built-in feature in .NET 5. They perform code generation at compile time, providing the ability to add source code to your project automatically.

# Source generators

Source generators were added as a built-in feature in .NET 5. They perform code generation at compile time, providing the ability to add source code to your project automatically. This opens up a vast realm of possibilities, but the ability to use source generators to replace things that would otherwise need to be done using reflection is a firm favourite.

In .NET 6, a new API was introduced for creating "incremental generators". These have broadly the same functionality as the source generators in .NET 5, but they are designed to take advantage of caching to significantly improve performance, so that your IDE doesn't slow down! The main downside to incremental generators is that they are only supported in the .NET 6 SDK (and so only in VS 2022).

# Enums and ToString()

The simple enum in c# is a handy little think for representing a choice of options. Under the hood, it's represented by a numeric value (typically an int), but instead of having to remember in your code that 0 represents "Red" and 1 represents "Blue", you can use an enum that holds that information for you:

<pre>
<code>
public enum Color
{
    Red = 0,
    Blue = 1,
}
</code>
</pre>

In your code, you pass instances of the enum Color around, but behind the scenes the runtime really just uses an int. The trouble is, sometimes you want to get the name of the color. The built-in way to do that is called ToString()

<pre>
<code>
void PrintColour(Color color)
{
    Console.WriteLine("You chose " + color.ToString());
}
</code>
</pre>

But it's maybe less common knowledge that this is sloooow. We'll look at how slow shortly, but first we'll look at a fast implementation, using modern C#:

<pre>
<code>
public static class EnumExtensions
{
    public static string ToStringFast(this Color color)
        => color switch
        {
            Color.Red => nameof(Color.Red),
            Color.Blue => nameof(Color.Blue),
            _ => color.ToString(),
        };
}
</code>
</pre>

This simple switch statement checks for each of the known values of Color and uses nameof to return the textual representation of the enum. If it's an unknown value, then the underlying value is returned as a string.

| You always have to be careful about these unknown values: for example this is valid C# PrintColor((Color)123)

If we compare this simple switch statement to the default ToString() implementation using BenchmarkDotNet for a known color, you can see how much faster our implementation is:

![image](https://user-images.githubusercontent.com/59767834/145995670-1bafe808-82ea-4b56-862b-a736b99b6f5b.png)

First off, it's worth pointing out that ToString() in .NET 6 is over 30× faster and allocates only a quarter of the bytes than the method in .NET Framework! Compare that to the "fast" version though, and it's still super slow!

As fast as it is, creating the ToStringFast() method is a bit of a pain, as you have to make sure to keep it up to date as your enum changes. Luckily, that's a perfect usecase for a source generator!

# Creating the Source generator project

To get started we need to create a C# project. Source generators must target netstandard2.0, and you'll need to add some standard packages to get access to the source generator types.

Start by creating a class library. The following uses the sdk to create a solution and a project in the current folder:

<pre>
<code>
dotnet new sln -n SG.EnumGenerators
dotnet new classlib -o ./src/SG.EnumGenerators
dotnet sln add ./src/SG.EnumGenerators
</code>
</pre>

Replace the contents of SG.EnumGenerators.csproj with the following:

<pre>
<code>
&lt;Project Sdk="Microsoft.NET.Sdk"&gt;
	&lt;PropertyGroup&gt;
		&lt;!-- Source generators must target netstandard 2.0 --&gt;
		&lt;TargetFramework&gt;netstandard2.0&lt;/TargetFramework&gt;
		&lt;!-- We don't want to reference the source generator dll directly in consuming projects --&gt;
		&lt;IncludeBuildOutput&gt;false&lt;/IncludeBuildOutput&gt;
		&lt;!-- New project, why not! --&gt;
		&lt;Nullable&gt;enable&lt;/Nullable&gt;
		&lt;ImplicitUsings&gt;true&lt;/ImplicitUsings&gt;
		&lt;LangVersion&gt;Latest&lt;/LangVersion&gt;
	&lt;/PropertyGroup&gt;
	&lt;!-- The following libraries include the source generator interfaces and types we need --&gt;
	&lt;ItemGroup&gt;
		&lt;PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.2" PrivateAssets="all" /&gt;
		&lt;PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.0.1" PrivateAssets="all" /&gt;
	&lt;/ItemGroup&gt;
	&lt;!-- This ensures the library will be packaged as a source generator when we use `dotnet pack` --&gt;
	&lt;ItemGroup&gt;
		&lt;None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true"
			PackagePath="analyzers/dotnet/cs" Visible="false" /&gt;
	&lt;/ItemGroup&gt;
&lt;/Project&gt;
</code>
</pre>

# Collecting details about enums

At a minimum, we need to know:
- The full Type name of the enum
- The name of all the values

And that's pretty much it. There's lots of more information we could collect for a better user experience, but for now, we'll stick with that, to get something working. Given that, we can create a simple type to hold the details about the enums we discover:

<pre>
<code>
public readonly struct EnumToGenerate
{
    public readonly string Name;
    public readonly List<string> Values;

    public EnumToGenerate(string name, List<string> values)
    {
        Name = name;
        Values = values;
    }
}
</code>
</pre>

# Adding a marker attribute

We also need to think about how we are going to choose which enums to generate the extension methods for. We could do it for every enum in the project, but that seems a bit overkill. Instead, we could use a "marker attribute". A marker attribute is a simple attribute that doesn't have any functionality, and only exists so that something else (in this case, our source generator) can locate the type. Users would decorate their enum with the attribute, so we know to generate the extension method for it:

<pre>
<code>
[EnumExtensions] // Our marker attribute
public enum Colour
{
    Red = 0,
    Blue = 1,
}
</code>
</pre>

We'll create a simple marker attribute as shown below, but we're not going to define this attribute in code directly. Rather, we're creating a string containing c# code for the [EnumExtensions] marker attribute. We'll make the source generator automatically add this to the compilation of consuming projects at runtime so the attribute is available.

<pre>
<code>
public static class SourceGenerationHelper
{
	public const string Attribute = @"
	namespace SG.EnumGenerators
	{
	[System.AttributeUsage(System.AttributeTargets.Enum)]
	public class EnumExtensionsAttribute : System.Attribute
	{
	}
	}";
}
</code>
</pre>

