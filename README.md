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

# Creating the incremental source generator

To create an incremental source generator, you need to do 3 things:
- Include the Microsoft.CodeAnalysis.CSharp package in your project. Note that incremental generators were introduced in version 4.0.0, and are only supported in .NET 6/VS 2022.
- Create a class that implements IIncrementalGenerator
- Decorate the class with the [Generator] attribute

We've already done the first step, so let's create our EnumGenerator implementation:

<pre>
<code>
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace SG.EnumGenerators;
[Generator]
public class EnumGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Add the marker attribute to the compilation
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            "EnumExtensionsAttribute.g.cs",
            SourceText.From(SourceGenerationHelper.Attribute, Encoding.UTF8)));

        // TODO: implement the remainder of the source generator
    }
}
</code>
</pre>

IIncrementalGenerator only requires you implement a single method, Initialize(). In this method you can register your "static" source code (like the marker attributes), as well as build a pipeline for identifying syntax of interest, and transforming that syntax into source code.

In the implementation above, we've already added the code that registers our marker attribute to the compilation. Next, we'll build up the code to identify enums that have been decorated with the marker attribute.

# Building the incremental generator pipeline

One of the key things to remember when building source generators, is that there are a lot of changes happening when you're writing source code. Every change the user makes could trigger the source generator to run again, so you have to be efficient, otherwise you're going to kill the user's IDE experience

The design of incremental generators is to create a "pipeline" of transforms and filters, memoizing the results at each layer to avoid re-doing work if there are no changes. It's important that the stage of the pipeline is very efficient, as this will be called a lot, ostensibly for every source code change. Later layers need to remain efficient, but there's more leeway there. If you've designed your pipeline well, later layers will only be called when users are editing code that matters to you.

https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md

With that in mind, we'll create a simple generator pipeline that does the following:

- Filter syntax to only enums which have one or more attributes. This should be very fast, and will contain all the enums we're interested in.
- Filter syntax to only enums which have the [EnumExtensions] attribute. This is slightly more costly than the first stage, as it uses the semantic model (not just syntax), but is still not very expensive.
- Extract all the information we need using the Compilation. This is the most expensive step, and it combines the Compilation for the project with the previously-selected enum syntax. This is where we can create our collection of EnumToGenerate, generate the source, and register it as a source generator output.

In code, the pipeline is shown below. The three steps above correspond to the IsSyntaxTargetForGeneration(), GetSemanticTargetForGeneration() and Execute() methods respectively, which we'll see next.

The first stage of the pipeline uses CreateSyntaxProvider() to filter the incoming list of syntax tokens. The predicate, IsSyntaxTargetForGeneration(), provides a first layer of filtering. The transform, GetSemanticTargetForGeneration(), can be used to transform the syntax tokens, but in this case we only use it to provide additional filtering after the predicate. The subsequent Where() clause looks like LINQ, but it's actually a method on IncrementalValuesProvider which does that second layer of filtering for us.

The next stage of the pipeline simply combines our collection of EnumDeclarationSyntax emitted from the first stage, with the current Compilation.

Finally, we use the combined tuple of (Compilation, ImmutableArray<EnumDeclarationSyntax>) to actually generate the source code for the EnumExtensions class, using the Execute() method.

