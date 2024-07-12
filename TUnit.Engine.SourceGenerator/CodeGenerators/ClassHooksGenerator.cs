﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Engine.SourceGenerator.Enums;
using TUnit.Engine.SourceGenerator.Extensions;
using TUnit.Engine.SourceGenerator.Models;

namespace TUnit.Engine.SourceGenerator.CodeGenerators;

[Generator]
internal class ClassHooksGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var setUpMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "TUnit.Core.BeforeAllTestsInClassAttribute",
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);
        
        var cleanUpMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "TUnit.Core.AfterAllTestsInClassAttribute",
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);
        
        context.RegisterSourceOutput(setUpMethods,
            (productionContext, model) => Execute(productionContext, model, HookType.SetUp));
        context.RegisterSourceOutput(cleanUpMethods,
            (productionContext, model) => Execute(productionContext, model, HookType.CleanUp));
    }

    static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax;
    }

    static HooksDataModel? GetSemanticTargetForGeneration(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        if (!methodSymbol.IsStatic)
        {
            return null;
        }

        return new HooksDataModel
        {
            MethodName = methodSymbol.Name,
            FullyQualifiedTypeName = methodSymbol.ContainingType.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix),
            MinimalTypeName = methodSymbol.ContainingType.Name,
            ParameterTypes = methodSymbol.Parameters.Select(x => x.Type.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix)).ToArray(),
            HasTimeoutAttribute = methodSymbol.HasTimeoutAttribute()
        };
    }

    private void Execute(SourceProductionContext context, HooksDataModel? model, HookType hookType)
    {
        if (model is null)
        {
            return;
        }
        
        var className = $"ClassHooks_{model.MinimalTypeName}_{Guid.NewGuid():N}";

        using var sourceBuilder = new SourceCodeWriter();
                
        sourceBuilder.WriteLine("// <auto-generated/>");
        sourceBuilder.WriteLine("using global::System.Linq;");
        sourceBuilder.WriteLine("using global::System.Reflection;");
        sourceBuilder.WriteLine("using global::System.Runtime.CompilerServices;");
        sourceBuilder.WriteLine("using global::TUnit.Core;");
        sourceBuilder.WriteLine("using global::TUnit.Core.Interfaces;");
        sourceBuilder.WriteLine("using global::TUnit.Engine;");
        sourceBuilder.WriteLine("using global::TUnit.Engine.Helpers;");
        sourceBuilder.WriteLine("using global::TUnit.Engine.Hooks;");
        sourceBuilder.WriteLine();
        sourceBuilder.WriteLine("namespace TUnit.Engine;");
        sourceBuilder.WriteLine();
        sourceBuilder.WriteLine("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        sourceBuilder.WriteLine($"file class {className}");
        sourceBuilder.WriteLine("{");
        sourceBuilder.WriteLine("[ModuleInitializer]");
        sourceBuilder.WriteLine("public static void Initialise()");
        sourceBuilder.WriteLine("{");

        if (hookType == HookType.SetUp)
        {
            sourceBuilder.WriteLine(
                $$"""
                  ClassHookOrchestrator.RegisterSetUp(typeof({{model.FullyQualifiedTypeName}}), new StaticMethod
                  		{ 
                             MethodInfo = typeof({{model.FullyQualifiedTypeName}}).GetMethod("{{model.MethodName}}", 0, [{{string.Join(", ", model.ParameterTypes.Select(x => $"typeof({x})"))}}]),
                             Body = cancellationToken => AsyncConvert.Convert(() => {{model.FullyQualifiedTypeName}}.{{model.MethodName}}({{GenerateContextObject(model)}}))
                  		});
                  """);
        }
        else if (hookType == HookType.CleanUp)
        {
            sourceBuilder.WriteLine(
                $$"""
                 ClassHookOrchestrator.RegisterCleanUp(typeof({{model.FullyQualifiedTypeName}}), new StaticMethod
                 		{ 
                             MethodInfo = typeof({{model.FullyQualifiedTypeName}}).GetMethod("{{model.MethodName}}", 0, [{{string.Join(", ", model.ParameterTypes.Select(x => $"typeof({x})"))}}]),
                             Body = cancellationToken => AsyncConvert.Convert(() => {{model.FullyQualifiedTypeName}}.{{model.MethodName}}({{GenerateContextObject(model)}}))
                 		});
                 """);
        }

        sourceBuilder.WriteLine("}");
        sourceBuilder.WriteLine("}");

        context.AddSource($"{className}.Generated.cs", sourceBuilder.ToString());
    }

    private string GenerateContextObject(HooksDataModel model)
    {
        List<string> args = [];
        
        foreach (var type in model.ParameterTypes)
        {
            if (type == WellKnownFullyQualifiedClassNames.ClassHookContext.WithGlobalPrefix)
            {
                args.Add($"TUnit.Engine.Hooks.ClassHookOrchestrator.GetClassHookContext(typeof({model.FullyQualifiedTypeName}))");
            }
            
            if (type == WellKnownFullyQualifiedClassNames.CancellationToken.WithGlobalPrefix)
            {
                args.Add("cancellationToken");
            }
        }

        return string.Join(", ", args);
    }
}