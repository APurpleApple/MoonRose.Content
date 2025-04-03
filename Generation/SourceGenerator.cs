using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Threading;

namespace MoonRose.Content
{
    [Generator(LanguageNames.CSharp)]
    internal class SourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext initContext)
        {
            initContext.RegisterSourceOutput(ExtractClasses(initContext), action: GenerateClasses);

            initContext.RegisterSourceOutput(ExtractClassesCollect(initContext), action: GenerateDB);

            initContext.RegisterPostInitializationOutput(i =>
            {
                i.AddSource("ContentAttribute.g.cs", Generator.GenerateContentAttribute());
            });

            var a = new IncrementalValuesProvider<ContentClassToGenerate>();
        }

        private static IncrementalValueProvider<ImmutableArray<ContentClassToGenerate?>> ExtractClassesCollect(in IncrementalGeneratorInitializationContext context)
                => context.SyntaxProvider.CreateSyntaxProvider(predicate: static (n, _) => n is ClassDeclarationSyntax || n is StructDeclarationSyntax || n is RecordDeclarationSyntax, transform: GetClassDetails).Collect();

        private static IncrementalValuesProvider<ContentClassToGenerate?> ExtractClasses(in IncrementalGeneratorInitializationContext context)
                => context.SyntaxProvider.CreateSyntaxProvider(predicate: static (n, _) => n is ClassDeclarationSyntax || n is StructDeclarationSyntax || n is RecordDeclarationSyntax, transform: GetClassDetails);

        private static ContentClassToGenerate? GetClassDetails(GeneratorSyntaxContext generatorSyntaxContext, CancellationToken cancellationToken)
            => SyntaxExtractor.ExtractClass(context: generatorSyntaxContext, cancellationToken: cancellationToken);

        private static void GenerateClasses(SourceProductionContext sourceProductionContext, ContentClassToGenerate? classEnumGeneration)
        {
            if (classEnumGeneration is null) return;
            if (!classEnumGeneration.Value.GenerateStorage) return;

            string className = Generator.GenerateKeyStruct(classDeclaration: classEnumGeneration.Value, out var codeBuilder);
            sourceProductionContext.AddSource( className + ".g.cs", sourceText: codeBuilder.Text);

            className = Generator.GenerateContentStorage(classDeclaration: classEnumGeneration.Value, out codeBuilder);
            sourceProductionContext.AddSource(className + ".g.cs", sourceText: codeBuilder.Text);
        }

        private static void GenerateDB(SourceProductionContext sourceProductionContext, ImmutableArray<ContentClassToGenerate?> classEnumGeneration)
        {
            sourceProductionContext.AddSource("DB.g.cs", sourceText: Generator.GenerateDBClass(classEnumGeneration));
        }
    }
}