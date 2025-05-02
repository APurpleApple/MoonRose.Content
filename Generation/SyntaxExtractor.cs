using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace MoonRose.Content;

internal static class SyntaxExtractor
{
    public static string debug = "";
    public static ContentClassToGenerate? ExtractClass(in GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var classSymbol = (INamedTypeSymbol)context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken: cancellationToken)!;

        AttributeData attribute = IsContentClass(context, classSymbol, cancellationToken);
        if (attribute == null) return null; 

        cancellationToken.ThrowIfCancellationRequested();
        bool hasLoadMethod =  ContainsLoadMethod(context, classSymbol, cancellationToken);
        bool hasOnLoadMethod = ContainsOnLoadMethod(context, classSymbol, cancellationToken);

        string name = classSymbol.Name;
        string loaderName = name;
        string nameSpace = classSymbol.ContainingNamespace.ToDisplayString();
        string loaderNameSpace = nameSpace;
        bool isDisposable = InheritFromInterfaceWithName("IDisposable", classSymbol);

        if (attribute.AttributeClass.IsGenericType)
        {
            if (attribute.AttributeClass.TypeArguments.Length > 0)
            {
                name = attribute.AttributeClass.TypeArguments[0].Name;
                nameSpace = attribute.AttributeClass.TypeArguments[0].ContainingNamespace.ToDisplayString();
                isDisposable = InheritFromInterfaceWithName("IDisposable", attribute.AttributeClass.TypeArguments[0]);
            }
        }

        var dependencies = GetContentDependencies(context, classSymbol, cancellationToken);

        return new( name, loaderName, nameSpace, loaderNameSpace, context.Node.GetLocation(), isDisposable, hasLoadMethod, hasOnLoadMethod, attribute, dependencies, debug);
    }

    private static bool InheritFromInterfaceWithName(string name, ITypeSymbol type)
    {
        foreach (var item in type.AllInterfaces)
        {
            if (item.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLoadMethod(in GeneratorSyntaxContext context, INamedTypeSymbol classSymbol, CancellationToken cancellationToken)
    {
        var members = classSymbol.GetMembers();

        foreach (var item in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Name != "LoadFromPath") continue;
            if (item.Kind != SymbolKind.Method) continue;
            if (item.DeclaredAccessibility != Accessibility.Public) continue;
            if (!item.IsStatic) continue;
            
            var methodSymbol = item as IMethodSymbol;
            if (methodSymbol == null) continue;
            if (!methodSymbol.ReturnsVoid) continue;
            var parameters = methodSymbol.Parameters;
            
            if (parameters.Length != 3) continue;
            if (parameters[0].Type.Name != "TitleStorage") continue;
            if (parameters[1].Type.Name != "GraphicsDevice") continue;
            debug = parameters[2].Type.Name;
            if (parameters[2].Type.Name != "String") continue;

            return true;
        }

        return false;
    }
    private static bool ContainsOnLoadMethod(in GeneratorSyntaxContext context, INamedTypeSymbol classSymbol, CancellationToken cancellationToken)
    {
        var members = classSymbol.GetMembers();

        foreach (var item in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Name != "OnDBLoad") continue;
            if (item.Kind != SymbolKind.Method) continue;
            if (item.DeclaredAccessibility != Accessibility.Public) continue;
            if (!item.IsStatic) continue;

            var methodSymbol = item as IMethodSymbol;
            if (methodSymbol == null) continue;
            if (!methodSymbol.ReturnsVoid) continue;
            var parameters = methodSymbol.Parameters;

            if (parameters.Length != 2) continue;
            if (parameters[0].Type.Name != "TitleStorage") continue;
            if (parameters[1].Type.Name != "GraphicsDevice") continue;

            return true;
        }

        return false;
    }

    private static List<string> GetContentDependencies(in GeneratorSyntaxContext context, INamedTypeSymbol classSymbol, CancellationToken cancellationToken)
    {
        
        var members = classSymbol.GetMembers();

        List<string> result = new();

        foreach (var item in members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Kind != SymbolKind.Field) continue;

            IFieldSymbol field = (IFieldSymbol)item;
            if (field.Type.Name.Contains("Key"))
            {
                result.Add(field.Type.Name);
            }
            else if (field.Type is IArrayTypeSymbol arrayTypeSymbol)
            {
                if (arrayTypeSymbol.ElementType.Name.Contains("Key"))
                {
                    result.Add(arrayTypeSymbol.ElementType.Name);
                }
            }
        }

        var attributes = classSymbol.GetAttributes();
        foreach (var item in attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.AttributeClass.ContainingNamespace.ToDisplayString() != "MoonRose.Content") continue;
            if (item.AttributeClass.Name != "ContentDependencies") continue;

            var deps = item.ConstructorArguments[0].Values;
            for (int i = 0; i < deps.Length; i++)
            {
                result.Add(((INamedTypeSymbol)deps[i].Value).Name + "Key");
            }

            break;
        }

        return result;
    }

    private static AttributeData? IsContentClass(in GeneratorSyntaxContext context, INamedTypeSymbol classSymbol, CancellationToken cancellationToken)
    {
        var attributes = classSymbol.GetAttributes();

        foreach (var item in attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCodeGenerationAttribute(item)) return item;
        }

        return null;
    }

    private static bool IsCodeGenerationAttribute(AttributeData item)
    {
        if (item.AttributeClass is null)
            return false;
        if (item.AttributeClass.ContainingNamespace.ToDisplayString() != "MoonRose.Content")
            return false;

        if (item.AttributeClass.Name == "ContentAttribute")
            return true;
        if (item.AttributeClass.Name == "ContentLoaderAttribute")
            return true;

        return false;
    }
}
