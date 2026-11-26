using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A game branch's assembly file (each decompile's assembly/sts2.dll, decision #165) loaded for reading only: its types through a
/// MetadataLoadContext, its method bodies' instructions named through its metadata (the context can't resolve tokens), so the card
/// program reader (decision #182) reads main's code the way the mod reads the loaded game's.
/// </summary>
internal sealed class BranchCode
{
    public static string BetaPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", "sts2-decompiled", "assembly", "sts2.dll");
    public static string MainPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", "sts2-decompiled-main", "assembly", "sts2.dll");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<BranchCode>> Shared = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The branch's code at <paramref name="path"/>, loaded once a test run and shared by every test that reads it: nothing in it changes once
    /// loaded (its types, and each method's instructions decoded once, <see cref="Read"/>), so the whole-game scans don't each load and decode
    /// the same assembly again. Never disposed: it lives as long as the test run.
    /// </summary>
    public static BranchCode Of(string path) => Shared.GetOrAdd(Path.GetFullPath(path), full => new Lazy<BranchCode>(() => new BranchCode(full))).Value;

    private readonly MetadataLoadContext _context;
    private readonly FileStream _stream;
    private readonly PEReader _portable;
    private readonly MetadataReader _reader;
    private readonly Names _names;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Module, int), List<CodeOp>> _decoded = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<object>> _readings = new(StringComparer.Ordinal);

    public Assembly Assembly { get; }

    /// <summary>
    /// A reading of this branch's code made once a test run (an owner board, the read models' specs...): <paramref name="make"/> runs once for
    /// <paramref name="key"/>, every caller gets what it made. What it makes must not be changed by its callers.
    /// </summary>
    public T Reading<T>(string key, Func<BranchCode, T> make) where T : class =>
        (T)_readings.GetOrAdd(key, _ => new Lazy<object>(() => make(this))).Value;

    /// <summary>A power's, relic's or orb's hooks read by the model reader (HookReader.Read), once a test run.</summary>
    public Sim.ModelReading HookReading(Type type) => Reading("hook reading " + type.FullName, code => Sim.HookReader.Read(type, code.Read));

    /// <summary>The read models of this branch (ReadModels.Specs over its types), once a test run.</summary>
    public List<Sim.ModelSpec> Specs() => Reading("specs", code => Sim.ReadModels.Specs(code.Assembly.GetTypes(), code.Read));

    /// <summary>The branch's owner board (which kinds of creature each power can be on), once a test run.</summary>
    public OwnerBoard Board() => Reading("owner board", code => new OwnerBoard(code));

    private BranchCode(string path)
    {
        var gameDir = Path.GetDirectoryName(typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly.Location)!;
        var paths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Concat(Directory.GetFiles(gameDir, "*.dll").Where(file => !Path.GetFileName(file).Equals("sts2.dll", StringComparison.OrdinalIgnoreCase)))
            .Append(path);
        _context = new MetadataLoadContext(new PathAssemblyResolver(paths));
        Assembly = _context.LoadFromAssemblyPath(path);
        _stream = File.OpenRead(path);
        _portable = new PEReader(_stream);
        _reader = _portable.GetMetadataReader();
        _names = new Names(_reader);
    }

    /// <summary>Every card class on this branch.</summary>
    public IEnumerable<Type> Cards() => Assembly.GetTypes()
        .Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Cards" && !type.IsAbstract && !type.IsNested && Derives(type, "CardModel"));

    /// <summary>Every model class on this branch (cards, relics, powers, potions, enchantments...), not nested.</summary>
    public IEnumerable<Type> Models() => Assembly.GetTypes()
        .Where(type => type.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) == true && !type.IsNested);

    /// <summary>An enum member's name on this branch (its numbers can differ from the loaded game's: main has no Wah tag).</summary>
    public string? EnumName(string enumType, long value)
    {
        var type = Assembly.GetTypes().FirstOrDefault(candidate => candidate.IsEnum && candidate.Name == enumType && candidate.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true);
        return type?.GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(field => field.IsLiteral && Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture) == value)?.Name;
    }

    private static bool Derives(Type type, string baseName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseName) return true;
        return false;
    }

    /// <summary>A method's instructions, decoded once a test run (as CodeOps' cache does for the running game): callers only read the list.</summary>
    public List<CodeOp> Read(MethodBase method) => _decoded.GetOrAdd((method.Module, method.MetadataToken), _ => Decode(method));

    private List<CodeOp> Decode(MethodBase method)
    {
        var code = method.GetMethodBody()?.GetILAsByteArray();
        if (code == null) return new List<CodeOp>();
        return CodeDecoder.Decode(code, token => Member(MetadataTokens.EntityHandle(token)), token =>
        {
            try { return _reader.GetUserString(MetadataTokens.UserStringHandle(token)); }
            catch { return null; }
        });
    }

    /// <summary>
    /// The method a call in <paramref name="caller"/>'s body names, when it's defined on this branch (a method of the same assembly,
    /// a generic one's definition included): null for anything elsewhere.
    /// </summary>
    public MethodBase? Callee(MethodBase caller, CodeOp call)
    {
        var code = caller.GetMethodBody()?.GetILAsByteArray();
        if (code == null || !call.IsCall) return null;
        var handle = MetadataTokens.EntityHandle(BitConverter.ToInt32(code, call.Offset + call.OpCode.Size));
        if (handle.Kind == HandleKind.MethodSpecification) handle = _reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind != HandleKind.MethodDefinition) return null;
        var token = MetadataTokens.GetToken(handle);
        var declaring = _reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType();
        if (Assembly.GetType(FullName(declaring)) is not { } type) return null;
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).FirstOrDefault(method => method.MetadataToken == token);
    }

    /// <summary>A type definition's reflection name (Namespace.Outer+Inner).</summary>
    private string FullName(TypeDefinitionHandle handle)
    {
        var type = _reader.GetTypeDefinition(handle);
        var name = _reader.GetString(type.Name);
        if (!type.GetDeclaringType().IsNil) return FullName(type.GetDeclaringType()) + "+" + name;
        var space = _reader.GetString(type.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }

    private CodeMember? Member(EntityHandle handle)
    {
        try
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var method = _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var (declaring, space) = _names.Type(method.GetDeclaringType());
                    var signature = method.DecodeSignature(_names, null);
                    return MethodMember(declaring, space, _reader.GetString(method.Name), signature, Array.Empty<string>());
                }
                case HandleKind.MemberReference:
                {
                    var reference = _reader.GetMemberReference((MemberReferenceHandle)handle);
                    var (declaring, space) = _names.Type(reference.Parent);
                    var name = _reader.GetString(reference.Name);
                    if (reference.GetKind() == MemberReferenceKind.Field)
                        return new CodeMember(CodeMemberKind.Field, declaring, name) { ValueType = reference.DecodeFieldSignature(_names, null), HasThis = true, Namespace = space };
                    return MethodMember(declaring, space, name, reference.DecodeMethodSignature(_names, null), Array.Empty<string>()) with { TypeArguments = _names.TypeArguments(reference.Parent) };
                }
                case HandleKind.MethodSpecification:
                {
                    var specification = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    var generic = specification.DecodeSignature(_names, null).Select(Names.Simple).ToArray();
                    var method = Member(specification.Method);
                    return method == null ? null : method with { GenericArguments = generic };
                }
                case HandleKind.FieldDefinition:
                {
                    var field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                    var (declaring, space) = _names.Type(field.GetDeclaringType());
                    return new CodeMember(CodeMemberKind.Field, declaring, _reader.GetString(field.Name))
                        { ValueType = field.DecodeSignature(_names, null), HasThis = (field.Attributes & FieldAttributes.Static) == 0, Namespace = space, Data = DataOf(field) };
                }
                case HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification:
                {
                    var (name, space) = _names.Type(handle);
                    return new CodeMember(CodeMemberKind.Type, "", name) { Namespace = space };
                }
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The bytes the compiler keeps a constant array's data in (an RVA field: its type's size from its class layout, or a primitive's), read from the file's
    /// section at its address, as RuntimeCode reads the loaded game's; null for any other field.
    /// </summary>
    private byte[]? DataOf(FieldDefinition field)
    {
        if ((field.Attributes & FieldAttributes.HasFieldRVA) == 0) return null;
        var blob = _reader.GetBlobReader(field.Signature);
        blob.ReadSignatureHeader();
        var size = blob.ReadSignatureTypeCode() switch
        {
            SignatureTypeCode.Byte or SignatureTypeCode.SByte or SignatureTypeCode.Boolean => 1,
            SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 or SignatureTypeCode.Char => 2,
            SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Single => 4,
            SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double => 8,
            SignatureTypeCode.TypeHandle when blob.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } type => _reader.GetTypeDefinition((TypeDefinitionHandle)type).GetLayout().Size,
            _ => 0,
        };
        if (size <= 0) return null;
        return _portable.GetSectionData(field.GetRelativeVirtualAddress()).GetContent(0, size).ToArray();
    }

    private static CodeMember MethodMember(string declaring, string space, string name, MethodSignature<string> signature, string[] generic) =>
        new(CodeMemberKind.Method, declaring, name)
        {
            GenericArguments = generic,
            ParameterCount = signature.ParameterTypes.Length,
            ParameterTypes = signature.ParameterTypes,
            HasThis = signature.Header.IsInstance,
            ReturnsValue = signature.ReturnType != "Void",
            ValueType = signature.ReturnType,
            Namespace = space,
        };

    /// <summary>Simple type names from metadata (no namespace, no generic arguments), as reflection's Type.Name gives them.</summary>
    private sealed class Names : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _reader;

        public Names(MetadataReader reader) => _reader = reader;

        public static string Simple(string name) => name;

        public (string Name, string Namespace) Type(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                {
                    var type = _reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    var space = _reader.GetString(type.Namespace);
                    if (space.Length == 0 && !type.GetDeclaringType().IsNil) space = Type(type.GetDeclaringType()).Namespace;
                    return (_reader.GetString(type.Name), space);
                }
                case HandleKind.TypeReference:
                {
                    var type = _reader.GetTypeReference((TypeReferenceHandle)handle);
                    var space = _reader.GetString(type.Namespace);
                    if (space.Length == 0 && type.ResolutionScope.Kind == HandleKind.TypeReference) space = Type((TypeReferenceHandle)type.ResolutionScope).Namespace;
                    return (_reader.GetString(type.Name), space);
                }
                case HandleKind.TypeSpecification:
                {
                    // A generic instance (List<CardModel>, AttackCommand...): its generic type's name.
                    var specification = _reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                    var blob = _reader.GetBlobReader(specification.Signature);
                    var decoded = new SignatureDecoder<string, object?>(new GenericNames(this), _reader, null).DecodeType(ref blob);
                    var tick = decoded.IndexOf('|');
                    return tick >= 0 ? (decoded[..tick], decoded[(tick + 1)..]) : (decoded, "");
                }
                default:
                    return ("", "");
            }
        }

        /// <summary>A generic instance's type arguments (PowerVar&lt;AimPower&gt;: "AimPower"); empty for any other type.</summary>
        public IReadOnlyList<string> TypeArguments(EntityHandle handle)
        {
            if (handle.Kind != HandleKind.TypeSpecification) return Array.Empty<string>();
            var blob = _reader.GetBlobReader(_reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            var arguments = new List<string>();
            new SignatureDecoder<string, object?>(new ArgumentNames(this, arguments), _reader, null).DecodeType(ref blob);
            return arguments;
        }

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Void => "Void",
            PrimitiveTypeCode.Boolean => "Boolean",
            PrimitiveTypeCode.Int32 => "Int32",
            PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.String => "String",
            PrimitiveTypeCode.Object => "Object",
            _ => typeCode.ToString(),
        };
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Type(handle).Name;
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Type(handle).Name;
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => Type(handle).Name;

        /// <summary>A generic instance's outermost type arguments, by their simple names, into the list given.</summary>
        private sealed class ArgumentNames : ISignatureTypeProvider<string, object?>
        {
            private readonly Names _names;
            private readonly List<string> _arguments;
            private int _depth;
            public ArgumentNames(Names names, List<string> arguments) { _names = names; _arguments = arguments; }
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            {
                if (_depth++ == 0) _arguments.AddRange(typeArguments);
                return genericType;
            }
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
            public string GetByReferenceType(string elementType) => elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType;
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => _names.GetPrimitiveType(typeCode);
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => _names.Type(handle).Name;
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => _names.Type(handle).Name;
            public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => _names.Type(handle).Name;
        }

        /// <summary>A type specification's generic type, as "Name|Namespace".</summary>
        private sealed class GenericNames : ISignatureTypeProvider<string, object?>
        {
            private readonly Names _names;
            public GenericNames(Names names) => _names = names;
            public string GetArrayType(string elementType, ArrayShape shape) => elementType;
            public string GetByReferenceType(string elementType) => elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr|";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index + "|";
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index + "|";
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType;
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => _names.GetPrimitiveType(typeCode) + "|System";
            public string GetSZArrayType(string elementType) => elementType;
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) { var (name, space) = _names.Type(handle); return name + "|" + space; }
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) { var (name, space) = _names.Type(handle); return name + "|" + space; }
            public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) { var (name, space) = _names.Type(handle); return name + "|" + space; }
        }
    }
}
