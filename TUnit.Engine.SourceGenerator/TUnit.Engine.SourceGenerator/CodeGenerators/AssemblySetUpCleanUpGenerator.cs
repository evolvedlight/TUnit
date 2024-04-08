﻿using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TUnit.Engine.SourceGenerator.CodeGenerators;


[Generator]
public class AssemblySetUpCleanUpGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var testMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null)
            .Collect();

        context.RegisterSourceOutput(testMethods, Execute);
    }

    static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };
    }

    static IMethodSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        if (context.Node is not MethodDeclarationSyntax)
        {
            return null;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(context.Node);

        if (symbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        if (!methodSymbol.IsStatic)
        {
            return null;
        }

        var attributes = methodSymbol.GetAttributes();

        if (!attributes.Any(x =>
                x.AttributeClass?.ToDisplayString(
                    DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                is WellKnownFullyQualifiedClassNames.AssemblySetUpAttribute
                or WellKnownFullyQualifiedClassNames.AssemblyCleanUpAttribute))
        {
            return null;
        }

        return methodSymbol;
    }

    private void Execute(SourceProductionContext context, ImmutableArray<IMethodSymbol?> methodSymbols)
    {
        foreach (var method in methodSymbols.OfType<IMethodSymbol>())
        {
            if (method.GetAttributes().Any(x =>
                    x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                    == WellKnownFullyQualifiedClassNames.AssemblySetUpAttribute))
            {
                var className = $"AssemblySetUp_{method.ContainingType.Name}_{Guid.NewGuid():N}";

                var code = $"global::TUnit.Engine.AssemblyHookOrchestrators.RegisterSetUp(() => global::TUnit.Engine.RunHelpers(() => {method.ContainingType.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix)}.{method.Name}()))";
                
                context.AddSource($"{className}.g.cs", WrapInClass(className, code));
            }
            
            if (method.GetAttributes().Any(x =>
                    x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                    == WellKnownFullyQualifiedClassNames.AssemblyCleanUpAttribute))
            {
                var className = $"AssemblyCleanUp_{method.ContainingType.Name}_{Guid.NewGuid():N}";

                var code = $"global::TUnit.Engine.AssemblyHookOrchestrators.RegisterCleanUp(global::TUnit.Engine.RunHelpers(() => {method.ContainingType.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix)}.{method.Name}()))";
                
                context.AddSource($"{className}.g.cs", WrapInClass(className, code));
            }
        }
    }
    
    private static string WrapInClass(string className, string methodCode)
    {
        return $$"""
                 // <auto-generated/>
                 using System.Linq;
                 using System.Reflection;
                 using System.Runtime.CompilerServices;

                 namespace TUnit.Engine;

                 file class AssemblySetUpCleanUp_{{className}}_{{Guid.NewGuid()}}
                 {
                     [ModuleInitializer]
                     public static void Initialise()
                     {
                          {{methodCode}}
                     }
                 }
                 """;
    }
}