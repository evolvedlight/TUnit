using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Engine.SourceGenerator.Extensions;

namespace TUnit.Engine.SourceGenerator;

/// <summary>
/// A sample source generator that creates C# classes based on the text file (in this case, Domain Driven Design ubiquitous language registry).
/// When using a simple text file as a baseline, we can create a non-incremental source generator.
/// </summary>
[Generator]
public class TestsSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        // No initialization required for this generator.
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var sourceBuilder = new StringBuilder("""
                                              // <auto-generated/>
                                              using System.Linq;
                                              using System.Runtime.CompilerServices;

                                              namespace TUnit.Engine;

                                              file class TestGenerator
                                              {
                                                  private static async global::System.Threading.Tasks.Task RunAsync(global::System.Action action)
                                                  {
                                                      action();
                                                      await global::System.Threading.Tasks.Task.CompletedTask;
                                                  }
                                                  
                                                  private static async global::System.Threading.Tasks.Task RunAsync(global::System.Func<global::System.Threading.Tasks.Task> action)
                                                  {
                                                      await action();
                                                  }
                                                  
                                                  private static async global::System.Threading.Tasks.ValueTask RunAsync(global::System.Func<global::System.Threading.Tasks.ValueTask> action)
                                                  {
                                                      await action();
                                                  }
                                              
                                                  private static async global::System.Threading.Tasks.Task RunSafelyAsync(global::System.Action action, global::System.Collections.Generic.List<global::System.Exception> exceptions)
                                                  {
                                                    try
                                                    {
                                                        action();
                                                        await global::System.Threading.Tasks.Task.CompletedTask;
                                                    }
                                                    catch (global::System.Exception exception)
                                                    {
                                                        exceptions.Add(exception);
                                                    }
                                                  }
                                                  
                                                  private static async global::System.Threading.Tasks.Task RunSafelyAsync(global::System.Func<global::System.Threading.Tasks.Task> action, global::System.Collections.Generic.List<global::System.Exception> exceptions)
                                                  {
                                                    try
                                                    {
                                                        await action();
                                                    }
                                                    catch (global::System.Exception exception)
                                                    {
                                                        exceptions.Add(exception);
                                                    }
                                                  }
                                                  
                                                  private static async global::System.Threading.Tasks.ValueTask RunSafelyAsync(global::System.Func<global::System.Threading.Tasks.ValueTask> action, global::System.Collections.Generic.List<global::System.Exception> exceptions)
                                                  {
                                                    try
                                                    {
                                                        await action();
                                                    }
                                                    catch (global::System.Exception exception)
                                                    {
                                                        exceptions.Add(exception);
                                                    }
                                                  }
                                                    
                                                  [ModuleInitializer]
                                                  public static void Initialise()
                                                  {
                                              
                                              """);
        foreach (var method in context.Compilation
                     .SyntaxTrees
                     .SelectMany(st =>
                         st.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                             .Select(m => new Method(st, m)))
                     .Where(x => x.MethodDeclarationSyntax.DescendantNodes().OfType<AttributeSyntax>().Any()))
        {
            ProcessTests(sourceBuilder, context, method);
        }

        sourceBuilder.AppendLine("""
                                    }
                                 }
                                 """);

        context.AddSource("TestInitializer.g.cs", sourceBuilder.ToString());
    }

    private void ProcessTests(StringBuilder sourceBuilder, GeneratorExecutionContext context, Method method)
    {
        var semanticModel = context.Compilation.GetSemanticModel(method.SyntaxTree);

        var symbol = semanticModel.GetDeclaredSymbol(method.MethodDeclarationSyntax)
                     ?? semanticModel.GetSymbolInfo(method.MethodDeclarationSyntax).Symbol;

        if (symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        if (methodSymbol.ContainingType.IsAbstract)
        {
            return;
        }

        var attributes = symbol.GetAttributes();

        var isAwaitable = methodSymbol.IsAsync
                                    || methodSymbol.IsAwaitableNonDynamic(semanticModel, method.MethodDeclarationSyntax.SpanStart);
        
        foreach (var attributeData in attributes)
        {
            switch (attributeData.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix))
            {
                case "global::TUnit.Core.TestAttribute":
                    foreach (var classInvocation in GenerateClassInvocations(methodSymbol.ContainingType))
                    {
                        sourceBuilder.AppendLine(
                            GenerateTestInvocationCode(semanticModel, methodSymbol, classInvocation, [], isAwaitable)
                        );
                    }
                    break;
                case "global::TUnit.Core.DataDrivenTestAttribute":
                    break;
                case "global::TUnit.Core.DataSourceDrivenTestAttribute": 
                    break;
                case "global::TUnit.Core.CombinativeTestAttribute": 
                    break;
            }
        }
    }

    private string GenerateTestInvocationCode(
        SemanticModel semanticModel,
        IMethodSymbol methodSymbol, 
        string classInvocation,
        IEnumerable<string> methodArguments,
        bool isMethodAwaitable)
    {
        var testId = GetTestId(methodSymbol);
        
        var methodAwaitablePrefix = isMethodAwaitable? "await " : string.Empty;
        
        var classType = methodSymbol.ContainingType;

        var disposeCall = GenerateDisposeCall(classType);

        var fullyQualifiedClassType = classType.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix);
        return $$"""
                        global::TUnit.Core.TestDictionary.AddTest("{{testId}}", () => global::System.Threading.Tasks.Task.Run(async () =>
                        {
                            {{fullyQualifiedClassType}} classInstance = null!;
                            var teardownExceptions = new global::System.Collections.Generic.List<global::System.Exception>();
                            try
                            {
                 {{OneTimeSetUpWriter.GenerateCode(classType)}}
                 {{classInvocation}};
                     
                                var methodInfo = global::TUnit.Core.Helpers.MethodHelpers.GetMethodInfo(classInstance.{{methodSymbol.Name}});
                     
                                using var testContext = new global::TUnit.Core.TestContext(new global::TUnit.Core.TestInformation()
                                {
                                    Categories = [{{string.Join(", ", GetCategories(methodSymbol))}}],
                                    ClassInstance = classInstance,
                                    ClassType = typeof({{fullyQualifiedClassType}}),
                                    Timeout = {{GetTimeOut(methodSymbol)}},
                                    TestClassArguments = classArgs,
                                    TestMethodArguments = [{{string.Join(", ", methodArguments)}}],
                                    TestClassParameterTypes = classInstance.GetType().GetConstructors().First().GetParameters().Select(x => x.ParameterType).ToArray(),
                                    TestMethodParameterTypes = methodInfo.GetParameters().Select(x => x.ParameterType).ToArray(),
                                    NotInParallelConstraintKeys = [],
                                    RepeatCount = {{GetRepeatCount(methodSymbol)}},
                                    RetryCount = {{GetRetryCount(methodSymbol)}},
                                    MethodInfo = methodInfo,
                                    TestName = "{{methodSymbol.Name}}",
                                    CustomProperties = new global::System.Collections.Generic.Dictionary<string, string>()
                                });
                                
                                global::TUnit.Core.TestDictionary.TestContexts.Value = testContext;
                                
                                // TODO: ITestAttribute ApplyToTest
                                // TODO: Run with retries
                                // TODO: Throw on Fail Reason Not Empty
                                // TODO: Skip on Skip Reason Not Empty
                                
                 {{SetUpWriter.GenerateCode(classType)}}
                                await RunAsync(() => classInstance.{{GenerateTestMethodInvocation(methodSymbol)}});
                            }
                            finally
                            {
                 {{CleanUpWriter.GenerateCode(classType)}}
                                var remainingTests = global::TUnit.Engine.OneTimeCleanUpOrchestrator.NotifyCompletedTestAndGetRemainingTestsForType(typeof({{fullyQualifiedClassType}}));
                                
                                if (remainingTests == 0)
                                {
                 {{OneTimeCleanUpWriter.GenerateCode(classType)}}
                                }
                                
                                {{disposeCall}}
                            }
                            
                            if (teardownExceptions.Any())
                            {
                                throw new global::System.AggregateException(teardownExceptions);
                            }
                        }));
                 """;
    }

    private static string GenerateDisposeCall(INamedTypeSymbol classType)
    {
        if (classType.IsAsyncDisposable())
        {
            return "await RunSafelyAsync(() => classInstance?.DisposeAsync() ?? global::System.Threading.Tasks.ValueTask.CompletedTask, teardownExceptions);";
        }
        
        if (classType.IsDisposable())
        {
            return "await RunSafelyAsync(() => classInstance?.Dispose(), teardownExceptions);";
        }

        return string.Empty;
    }

    private int GetRepeatCount(IMethodSymbol methodSymbol)
    {
        return GetMethodAndClassAttributes(methodSymbol)
            .FirstOrDefault(x => x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                                 == "global::TUnit.Core.RepeatAttribute")
            ?.ConstructorArguments.First().Value as int? ?? 0;
    }

    private int GetRetryCount(IMethodSymbol methodSymbol)
    {
        return GetMethodAndClassAttributes(methodSymbol)
            .FirstOrDefault(x => x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                                 == "global::TUnit.Core.RetryAttribute")
            ?.ConstructorArguments.First().Value as int? ?? 0;
    }

    private string GetTimeOut(IMethodSymbol methodSymbol)
    {
        var timeoutAttribute = GetMethodAndClassAttributes(methodSymbol)
            .FirstOrDefault(x => x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                                 == "global::TUnit.Core.TimeoutAttribute");

        if (timeoutAttribute is null)
        {
            return "null";
        }

        var timeoutMillis = (int)timeoutAttribute.ConstructorArguments.First().Value!;
        
        return $"global::System.TimeSpan.FromMilliseconds({timeoutMillis})";
    }

    private IEnumerable<string> GetCategories(IMethodSymbol methodSymbol)
    {
        return GetMethodAndClassAttributes(methodSymbol)
            .Where(x => x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                        == "global::TUnit.Core.TestCategoryAttribute")
            .Select(x => $"\"{x.ConstructorArguments.First().Value}\"");
    }

    private IEnumerable<AttributeData> GetMethodAndClassAttributes(IMethodSymbol methodSymbol)
    {
        return [..methodSymbol.GetAttributes(), ..methodSymbol.ContainingType.GetAttributes()];
    }

    private string GetTestId(IMethodSymbol methodSymbol)
    {
        var containingType =
            methodSymbol.ContainingType.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix);

        var methodName = methodSymbol.Name;

        return $"{containingType} | {methodName}";
    }

    private string GetDisposableUsingPrefix(INamedTypeSymbol type)
    {
        if (type.IsAsyncDisposable())
        {
            return "await using ";
        }

        if (type.IsDisposable())
        {
            return "using ";
        }

        return string.Empty;
    }

    private IEnumerable<string> GenerateClassInvocations(INamedTypeSymbol namedTypeSymbol)
    {
        var className =
            namedTypeSymbol.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix);
        
        if (namedTypeSymbol.InstanceConstructors.First().Parameters.IsDefaultOrEmpty)
        {
            yield return $"""
                                         object[] classArgs = [];
                                         classInstance = new {className}()
                          """;
        }

        foreach (var dataSourceDrivenTestAttribute in namedTypeSymbol.GetAttributes().Where(x =>
                     x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                         is "global::TUnit.Core.MethodDataAttribute"))
        {
            var arg = dataSourceDrivenTestAttribute.ConstructorArguments.Length == 1
                ? $"{className}.{dataSourceDrivenTestAttribute.ConstructorArguments.First().Value}()"
                : $"{dataSourceDrivenTestAttribute.ConstructorArguments[0].Value}.{dataSourceDrivenTestAttribute.ConstructorArguments[1].Value}()";

            yield return $"""
                                         var arg = {arg};
                                         object[] classArgs = [arg];
                                         classInstance = new {className}(arg)
                          """;
        }
        
        foreach (var classDataAttribute in namedTypeSymbol.GetAttributes().Where(x =>
                     x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                         is "global::TUnit.Core.ClassDataAttribute"))
        {
            yield return $"""
                                         var arg = new {classDataAttribute.ConstructorArguments.First().Value}();
                                         object[] classArgs = [arg];
                                         classInstance = new {className}(arg)
                          """;
        }
        
        foreach (var classDataAttribute in namedTypeSymbol.GetAttributes().Where(x =>
                     x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                         is "global::TUnit.Core.InjectAttribute"))
        {
            var genericType = classDataAttribute.AttributeClass!.TypeArguments.First();
            var fullyQualifiedGenericType =
                genericType.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix);
            var sharedArgument = classDataAttribute.NamedArguments.First(x => x.Key == "Shared").Value;

            if (sharedArgument.Type?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                is "global::TUnit.Core.None")
            {
                yield return $"""
                                             var arg = new {genericType.ToDisplayString(DisplayFormats.FullyQualifiedGenericWithGlobalPrefix)}();
                                             object[] classArgs = [arg];
                                             classInstance = new {className}(arg)
                              """;
            }
            
            if (sharedArgument.Type?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                is "global::TUnit.Core.Globally")
            {

                yield return $"""
                                             var arg = global::TUnit.Engine.TestDataContainer.InjectedSharedGlobally.GetOrAdd(typeof({fullyQualifiedGenericType}), x => new {fullyQualifiedGenericType}());
                                             object[] classArgs = [arg];
                                             classInstance = return new {className}(arg);
                              """;
            }
            
            if (sharedArgument.Type?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                is "global::TUnit.Core.ForClass")
            {

                yield return $"""
                                             var arg = global::TUnit.Engine.TestDataContainer.InjectedSharedPerClassType.GetOrAdd(new global::TUnit.Engine.Models.DictionaryTypeTypeKey(typeof({className}), typeof({fullyQualifiedGenericType})), x => new {fullyQualifiedGenericType}());
                                             object[] classArgs = [arg];
                                             classInstance = return new {className}(arg);
                              """;
            }
            
            if (sharedArgument.Type?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                is "global::TUnit.Core.ForKey")
            {
                var key = sharedArgument.Value?.GetType().GetProperty("Key")?.GetValue(sharedArgument.Value);
                yield return $"""
                                             var arg = global::TUnit.Engine.TestDataContainer.InjectedSharedPerKey.GetOrAdd(new global::TUnit.Engine.Models.DictionaryStringTypeKey("{key}", typeof({fullyQualifiedGenericType})), x => new {fullyQualifiedGenericType}());
                                             object[] classArgs = [arg];
                                             classInstance = return new {className}(arg);
                              """;
            }
        }
    }

    private string GenerateTestMethodInvocation(IMethodSymbol method, params string[] methodArguments)
    {
        var methodName = method.Name;

        var args = string.Join(", ", methodArguments);

        if (method.GetAttributes().Any(x =>
                x.AttributeClass?.ToDisplayString(DisplayFormats.FullyQualifiedNonGenericWithGlobalPrefix)
                    is "global::TUnit.Core.TimeoutAttribute"))
        {
            // TODO : We don't want Engine cancellation token? We want a new linked one that'll cancel after the specified timeout in the attribute
            if(string.IsNullOrEmpty(args))
            {
                return $"{methodName}(EngineCancellationToken.Token)";
            }

            return $"{methodName}({args}, EngineCancellationToken.Token)";
        }
        
        return $"{methodName}({args})";
    }
}

public record Method
{
    public SyntaxTree SyntaxTree { get; }
    public MethodDeclarationSyntax MethodDeclarationSyntax { get; }

    public Method(SyntaxTree syntaxTree, MethodDeclarationSyntax methodDeclarationSyntax)
    {
        SyntaxTree = syntaxTree;
        MethodDeclarationSyntax = methodDeclarationSyntax;
    }
}