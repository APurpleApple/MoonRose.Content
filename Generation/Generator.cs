using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace MoonRose.Content;

internal static class Generator
{
    public static SourceText GenerateDBClass(ImmutableArray<ContentClassToGenerate?> classEnumGeneration)
    {
        IEnumerable<ContentClassToGenerate> contentClasses = classEnumGeneration.Where(c => c.HasValue).Select(c=>c.Value);
        List<ContentClassToGenerate> sortedByDependency = Utils.TopologicalSort(contentClasses.Select(c=> (c, c.KeyClassName, c.ContentTypeDependencies)));

        CodeBuilder source = new();

        foreach (var item in contentClasses)
        {
            source = source.AppendLine($"// {item.KeyClassName} Dependencies:");
            foreach (var dep in item.ContentTypeDependencies)
            {
                source = source.AppendLine($"     //{dep}");
            }
        }

        HashSet<string> usings = [];
        usings.Add("System");
        usings.Add("System.Threading.Tasks");
        usings.Add("MoonWorks.Storage");
        usings.Add("MoonWorks");
        usings.Add("MoonWorks.Graphics");
        usings.Add("SDL3");
        usings.Add("System.Runtime.InteropServices");
        usings.Add("System.Collections.Generic");
        usings.Add("System.IO");
        usings.Add("System.Diagnostics");
        foreach (var item in contentClasses)
        {
            usings.Add(item.LoaderNameSpace);
        }

        source = AddUsingDeclarations(source, usings.ToArray());

        //source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();


        using (source.StartBlock($"public static partial class DB"))
        {
            foreach (var item in contentClasses.Where(i=>i.GenerateStorage))
            {
                source.AppendLine($"public static {item.StorageClassName} {item.StorageName} = new();");
            }
            source.AppendBlankLine();

            source.AppendLine($"private static TitleStorage Storage;");
            source.AppendLine($"private static GraphicsDevice GraphicsDevice;");
            source.AppendLine($"public static Action OnLoaded;");
            source.AppendLine($"private static HashSet<string> validExtensions = [];");
            source.AppendBlankLine();

            source.AppendLine($"public static int LoadQueueSize {{get; private set;}} = 0;");
            source.AppendLine($"public static int LoadQueueProgress {{get; private set;}} = 0;");

            using (source.StartBlock($"class LoadData"))
            {
                using (source.StartBlock($"public LoadData(TitleStorage storage)"))
                {
                    source.AppendLine($"this.storage = storage;");
                }

                source.AppendLine($"public Dictionary<string, List<string>> assets = [];");
                source.AppendLine($"public TitleStorage storage;");

            }

            using (source.StartBlock($"public static void Init(TitleStorage titleStorage, GraphicsDevice graphicsDevice)"))
            {
                source.AppendLine($"Storage = titleStorage;");
                source.AppendLine($"GraphicsDevice = graphicsDevice;");
                foreach (var item in contentClasses)
                {
                    if (item.Extension != "")
                    {
                        source.AppendLine($"validExtensions.Add(\"{item.Extension}\");");
                    }
                }
            }
            
            using (source.StartBlock($"static TitleStorage GetStorage(string path)"))
            {
                using (source.StartBlock($"if (Path.IsPathRooted(path))"))
                {
                    source.AppendLine($"return new TitleStorage(path);");
                }
                source.AppendLine($"return Storage;");
            }

            using (source.StartBlock($"public static unsafe void LoadAssets(string relativePath)"))
            {
                source.AppendLine($"Stopwatch sw = Stopwatch.StartNew();");
                source.AppendLine($"LoadData data = new(GetStorage(relativePath));");
                using (source.StartBlock($"foreach(string ext in validExtensions)"))
                {
                    source.AppendLine($"data.assets.Add(ext, []);");
                }
                using (source.StartBlock($"if (Path.IsPathRooted(relativePath))"))
                {
                    source.AppendLine($"relativePath = \"\";");
                }
                source.AppendLine($"ParseDirectories(relativePath, data);");
                source.AppendBlankLine();
                source.AppendLine($"LoadAssets(data.storage, data.assets);");
                source.AppendLine($"sw.Stop();");
                source.AppendLine($"Logger.LogInfo($\"Loaded {{LoadQueueSize}} assets in {{sw.ElapsedMilliseconds}} milliseconds.\");");

                source.AppendLine($"OnLoaded?.Invoke();");
            }

            using (source.StartBlock($"public static unsafe Task LoadAssetsAsync(string relativePath)"))
            {
                source.AppendLine($"Stopwatch sw = Stopwatch.StartNew();");
                source.AppendLine($"LoadData data = new(GetStorage(relativePath));");
                using (source.StartBlock($"foreach(string ext in validExtensions)"))
                {
                    source.AppendLine($"data.assets.Add(ext, []);");
                }

                using (source.StartBlock($"Task task = new Task(()=>"))
                {
                    source.AppendLine($"ParseDirectories(relativePath, data);");
                    source.AppendLine($"LoadAssets(data.storage, data.assets);");
                    source.AppendLine($"sw.Stop();");
                    source.AppendLine($"Logger.LogInfo($\"Loaded {{LoadQueueSize}} assets in {{sw.ElapsedMilliseconds}} milliseconds.\");");
                    source.AppendLine($"OnLoaded?.Invoke();");
                }
                source.AppendLine($");");

                source.AppendLine($"task.Start();");
                source.AppendLine($"return task;");
            }

            using (source.StartBlock($"private static unsafe void LoadAssets(TitleStorage storage, Dictionary<string, List<string>> assets)"))
            {
                source.AppendLine($"LoadQueueSize = 0;");
                source.AppendLine($"LoadQueueProgress = 0;");
                using (source.StartBlock($"foreach(var assetList in assets)"))
                {
                    source.AppendLine($"LoadQueueSize += assetList.Value.Count;");
                }
                source.AppendBlankLine();

                foreach (var item in sortedByDependency)
                {
                    source.AppendLine($"Console.WriteLine(\"load {item.Name}\");");
                    if (item.HasLoadFromPathMethod)
                    {
                        using (source.StartBlock($"foreach(var item in assets[\"{item.Extension}\"])"))
                        {
                            source.AppendLine($"{item.LoaderName}.LoadFromPath(storage, GraphicsDevice, item);");
                            source.AppendLine($"LoadQueueProgress ++;");
                        }
                    }
                    if (item.HasOnDBLoadMethod)
                    {
                        source.AppendLine($"{item.LoaderName}.OnDBLoad(storage, GraphicsDevice);");
                    }
                }

            }

            using (source.StartBlock($"private static unsafe void ParseDirectories(string relativePath, LoadData data)"))
            {
                foreach (var item in contentClasses)
                {
                    if (item.Extension != "" && item.TakesOverDirectory)
                    {
                        using (source.StartBlock($"if(data.storage.Exists(relativePath + \"/{item.Extension}\"))"))
                        {
                            source.AppendLine($"data.assets[\"{item.Extension}\"].Add(relativePath);");
                            source.AppendLine($"return;");
                        }
                    }
                }
                source.AppendLine($"SDL.SDL_EnumerateStorageDirectory(data.storage.Handle, relativePath, ParseInDirectory, (nint)(&data));");
            }

            using (source.StartBlock($"private static unsafe SDL.SDL_EnumerationResult ParseInDirectory(nint dataPtr, byte* dirname, byte* fname)"))
            {
                source.AppendLine($"string fileName = Marshal.PtrToStringUTF8((nint)fname);");
                source.AppendLine($"string dirName = Marshal.PtrToStringUTF8((nint)dirname);");
                source.AppendLine($"LoadData data = *(LoadData*)dataPtr;");

                source.AppendBlankLine();

                using (source.StartBlock($"if(!Path.HasExtension(fileName))"))
                {
                    source.AppendLine($"ParseDirectories(dirName + fileName, data);");
                }
                using (source.StartBlock($"else"))
                {
                    source.AppendLine($"string ext = Path.GetExtension(fileName);");
                    source.AppendLine($"string path = dirName + fileName;");

                    source.AppendLine($"if(!validExtensions.Contains(ext)) return SDL.SDL_EnumerationResult.SDL_ENUM_CONTINUE;");
                    source.AppendLine($"data.assets[ext].Add(path);");
                }

                source.AppendLine($"return SDL.SDL_EnumerationResult.SDL_ENUM_CONTINUE;");
            }

            using (source.StartBlock($"public static void Clear()"))
            {
                foreach (var item in contentClasses)
                {
                    if (item.GenerateStorage)
                    {
                        source.AppendLine($"{item.StorageName}.Clear();");
                    }
                }
            }
        }
        return source.Text;
    }

    public static SourceText GenerateContentAttribute()
    {
        CodeBuilder source = new();

        source = AddUsingDeclarations(source,
            "System"
            );

        source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();

        source = source.AppendLine($"[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]");
        using (source.StartBlock($"public class ContentAttribute(string fileExtension = \"\", bool takeOverDirectory = false, bool generateStorage = true, bool NewtonsoftSerializer = false) : Attribute"))
        {

        }
        source = source.AppendBlankLine();
        source = source.AppendLine($"[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]");
        using (source.StartBlock($"public class ContentLoaderAttribute<T>(string fileExtension = \"\", bool takeOverDirectory = false, bool generateStorage = true, bool NewtonsoftSerializer = false) : Attribute"))
        {

        }
        source = source.AppendBlankLine();
        source = source.AppendLine($"[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]");
        using (source.StartBlock($"public class ContentDependencies(params Type[] types) : Attribute"))
        {

        }
        return source.Text;
    }

    public static string GenerateKeyStruct(in ContentClassToGenerate classDeclaration, out CodeBuilder source)
    {
        string keyClassName = classDeclaration.Name + "Key";
        string storageClassName = classDeclaration.Name + "Storage";
        string storageDBName = classDeclaration.Name + "s";

        source = new();

        HashSet<string> usings = [];
        usings.Add("System");
        usings.Add("System.Text.Json");
        usings.Add("System.Text.Json.Serialization");
        usings.Add(classDeclaration.Namespace);
        if (classDeclaration.NewtonsoftSerializer) usings.Add("Newtonsoft.Json");
        source = AddUsingDeclarations(source, usings.ToArray());

        //source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();

        source = source.AppendLine($"[System.Text.Json.Serialization.JsonConverter(typeof({keyClassName}JsonConverter))]");
        if (classDeclaration.NewtonsoftSerializer)
        {
            source = source.AppendLine($"[Newtonsoft.Json.JsonConverter(typeof({keyClassName}NTSJsonConverter))]");
        }
        using (source.StartBlock($"public readonly record struct {keyClassName}(int ID = -1)"))
        {
            source.AppendLine($"public {classDeclaration.Name} Fetch() => DB.{storageDBName}.Get(this);");
            source.AppendLine($"public string GetName() => DB.{storageDBName}.GetName(this);");
            source.AppendLine($"public static {storageClassName} Storage => DB.{storageDBName};");
            source.AppendLine($"public static implicit operator {classDeclaration.Name}({keyClassName} k) => DB.{storageDBName}.Get(k);");
            source.AppendLine($"public static explicit operator string({keyClassName} k) => DB.{storageDBName}.GetName(k);");
            source.AppendLine($"public static explicit operator {keyClassName}(string s) => DB.{storageDBName}.LookupKey(s);");
            source.AppendLine($"public static readonly {keyClassName} Null = new {keyClassName}(-1);");
            source.AppendBlankLine();

            using (source.StartBlock($"public override string ToString()"))
            {
                using (source.StartBlock($"if (!DB.{storageDBName}.IsKeyValid(this))"))
                {
                    source.AppendLine($"return \"None\";");
                }
                source.AppendLine($"return DB.{storageDBName}.GetName(this);");
            }

            using (source.StartBlock($"public override int GetHashCode()"))
            {
                source.AppendLine($"return ID;");
            }

            using (source.StartBlock($"private class {keyClassName}JsonConverter : System.Text.Json.Serialization.JsonConverter<{keyClassName}>"))
            {
                using (source.StartBlock($"public override void Write(Utf8JsonWriter writer, {keyClassName} value, JsonSerializerOptions options)"))
                {
                    source.AppendLine($"writer.WriteStringValue({keyClassName}.Storage.GetName(value));");
                }

                using (source.StartBlock($"public override {keyClassName} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)"))
                {
                    source.AppendLine($"return {keyClassName}.Storage.LookupKey(reader.GetString());");
                }

                using (source.StartBlock($"public override void WriteAsPropertyName(Utf8JsonWriter writer, {keyClassName} value, JsonSerializerOptions options)"))
                {
                    source.AppendLine($"writer.WritePropertyName({keyClassName}.Storage.GetName(value));");
                }

                using (source.StartBlock($"public override {keyClassName} ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)"))
                {
                    source.AppendLine($"return {keyClassName}.Storage.LookupKey(reader.GetString());");
                }
            }

            if (classDeclaration.NewtonsoftSerializer)
            {
                using (source.StartBlock($"private class {keyClassName}NTSJsonConverter : Newtonsoft.Json.JsonConverter<{keyClassName}>"))
                {
                    using (source.StartBlock($"public override {keyClassName} ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, {keyClassName} existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)"))
                    {
                        source.AppendLine($"return {keyClassName}.Storage.LookupKey((string)reader.Value);");
                    }

                    using (source.StartBlock($"public override void WriteJson(Newtonsoft.Json.JsonWriter writer, {keyClassName} value, Newtonsoft.Json.JsonSerializer serializer)"))
                    {
                        source.AppendLine($"writer.WriteValue({keyClassName}.Storage.GetName(value));");
                    }
                }
            }
        }

        return keyClassName;
    }

    public static string GenerateContentStorage(in ContentClassToGenerate classDeclaration, out CodeBuilder source)
    {
        string storageClassName = classDeclaration.Name + "Storage";
        string keyClassName = classDeclaration.Name + "Key";
        string storageDBName = classDeclaration.Name + "s";

        source = AddUsingDeclarations(new(),
            classDeclaration.Namespace,
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "MoonWorks"
            );

        //source = source.AppendLine($"namespace MoonRose.Content;").AppendBlankLine();
        using (source.StartBlock($"public class {storageClassName}"))
        {
            source.AppendLine($"readonly Dictionary<string, {classDeclaration.Name}> lookup = [];");
            source.AppendLine($"readonly Dictionary<int, string> IDToName = [];");
            source.AppendLine($"readonly Dictionary<string, int> NameToID = [];");
            source.AppendLine($"{classDeclaration.Name}[] storage = [];");
            source.AppendLine($"int nextID = 0;");
            source.AppendLine($"//{classDeclaration.Debug}");
            source.AppendLine($"public int Count => nextID;");
            source.AppendLine($"{keyClassName} defaultKey = new {keyClassName}(0);");

            using (source.StartBlock($"public void SetDefaultKey({keyClassName} key)"))
            {
                source.AppendLine($"defaultKey = key;");
            }

            using (source.StartBlock($"public Dictionary<string, {classDeclaration.Name}> Debug_GetRegistry()"))
            {
                source.AppendLine($"return lookup.ToDictionary();");
            }

            using (source.StartBlock($"public IEnumerable<{keyClassName}> Debug_GetEntries()"))
            {
                source.AppendLine($"return IDToName.Keys.Select(s=>new {keyClassName}(s));");
            }

            using (source.StartBlock($"public {keyClassName} LookupKey(string name)"))
            {
                using (source.StartBlock($"if (!Contains(name))"))
                {
                    source.AppendLine($"Logger.LogError($\"Looked up unregistered {classDeclaration.Name} key {{name}}\");");
                    source.AppendLine($"return defaultKey;");
                }
                source.AppendLine($"return new {keyClassName}(NameToID[name]);");
            }

            using (source.StartBlock($"public bool IsKeyValid({keyClassName} key)"))
            {
                source.AppendLine($"return key.ID < nextID && key.ID >= 0;");
            }

            using (source.StartBlock($"public bool Contains(string name)"))
            {
                source.AppendLine($"return lookup.ContainsKey(name);");
            }

            using (source.StartBlock($"public {classDeclaration.Name} Lookup(string name)"))
            {
                
                source.AppendLine($"return lookup[name];");
            }

            using (source.StartBlock($"public {classDeclaration.Name} Get({keyClassName} key)"))
            {
                using (source.StartBlock($"if (!IsKeyValid(key))"))
                {
                    source.AppendLine($"return storage[defaultKey.ID];");
                }

                source.AppendLine($"return storage[key.ID];");
            }

            using (source.StartBlock($"public string GetName({keyClassName} key)"))
            {
                source.AppendLine($"return IDToName[key.ID];");
            }

            using (source.StartBlock($"public {keyClassName} Register(string name, {classDeclaration.Name} thing)"))
            {
                using (source.StartBlock($"if (NameToID.TryGetValue(name, out var existingID))"))
                {
                    source.AppendLine($"Logger.LogWarn($\"Overriding {classDeclaration.Name} {{name}} with new value!\");");
                    if (classDeclaration.Disposable)
                    {
                        source.AppendLine($"storage[existingID]?.Dispose();");
                    }
                    source.AppendLine($"lookup[name] = thing;");
                    source.AppendLine($"storage[existingID] = thing;");
                    source.AppendLine($"return new {keyClassName}(existingID);");
                }

                using (source.StartBlock($"if (nextID >= storage.Length)"))
                {
                    source.AppendLine($"Array.Resize(ref storage, (int)((nextID+1) * 2));");
                }

                source.AppendLine($"lookup.Add(name, thing);");
                source.AppendLine($"NameToID.Add(name, nextID);");
                source.AppendLine($"IDToName.Add(nextID, name);");
                source.AppendLine($"storage[nextID] = thing;");
                source.AppendLine($"var result = new {keyClassName}(nextID);");

                source.AppendLine($"nextID ++;");
                source.AppendLine($"return result;");
            }

            using (source.StartBlock($"public void Clear()"))
            {
                source.AppendLine($"lookup.Clear();");
                source.AppendLine($"IDToName.Clear();");
                source.AppendLine($"NameToID.Clear();");
                if (classDeclaration.Disposable)
                {
                    using (source.StartBlock($"foreach (var item in storage)"))
                    {
                        source.AppendLine($"item?.Dispose();");
                    }
                }
                source.AppendLine($"storage = [];");
                source.AppendLine($"nextID = 0;");
            }
        }

        return storageClassName;
    }

    private static CodeBuilder AddUsingDeclarations(CodeBuilder source)
        => AddUsingDeclarations(source: source, "System", "System.CodeDom.Compiler", "System.Collections.Generic");

    private static CodeBuilder AddUsingDeclarations(CodeBuilder source, params string[] namespaces)
    {
        return namespaces
            .OrderBy(keySelector: n => n, comparer: StringComparer.OrdinalIgnoreCase)
            .Aggregate(seed: source, func: (current, ns) => current.AppendLine($"using {ns};"))
            .AppendBlankLine();
    }

    private static string ClassWithNamespaceFormatter(ContentClassToGenerate d)
        => d.Namespace == "<global namespace>" ? d.Name : $"{d.Namespace}.{d.Name}";

    private static void GenerateAccessors(CodeBuilder source, in ContentClassToGenerate attribute, Func<ContentClassToGenerate, string> classNameFormatter, int index)
    {
        string className = classNameFormatter(attribute);
    }

    private static string ConvertAccessType(AccessType accessType)
        => accessType switch
        {
            AccessType.PUBLIC => "public",
            AccessType.PRIVATE => "private",
            AccessType.PROTECTED => "protected",
            AccessType.PROTECTED_INTERNAL => "protected internal",
            AccessType.INTERNAL => "internal",
            _ => throw new ArgumentOutOfRangeException(nameof(accessType), actualValue: accessType, message: "Unknown access type")
        };
}
