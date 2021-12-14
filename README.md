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
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<!-- Source generators must target netstandard 2.0 -->
		<TargetFramework>netstandard2.0</TargetFramework>
		<!-- We don't want to reference the source generator dll directly in consuming projects -->
		<IncludeBuildOutput>false</IncludeBuildOutput>
		<!-- New project, why not! -->
		<Nullable>enable</Nullable>
		<ImplicitUsings>true</ImplicitUsings>
		<LangVersion>Latest</LangVersion>
	</PropertyGroup>
	<!-- The following libraries include the source generator interfaces and types we need -->
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.2" PrivateAssets="all" />
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.0.1" PrivateAssets="all" />
	</ItemGroup>
	<!-- This ensures the library will be packaged as a source generator when we use `dotnet pack` -->
	<ItemGroup>
		<None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true"
			PackagePath="analyzers/dotnet/cs" Visible="false" />
	</ItemGroup>
</Project>
</code>
</pre>

