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
internal sealed class BranchCode : IDisposable
{
    public static string BetaPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", "sts2-decompiled", "assembly", "sts2.dll");
    public static string MainPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "git", "sts2-decompiled-main", "assembly", "sts2.dll");

    private readonly MetadataLoadContext _context;
    private readonly FileStream _stream;
    private readonly PEReader _portable;
    private readonly MetadataReader _reader;
    private readonly Names _names;

    public Assembly Assembly { get; }

    public BranchCode(string path)
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

    private static bool Derives(Type type, string baseName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseName) return true;
        return false;
    }

    public List<CodeOp> Read(MethodBase method)
    {
        var code = method.GetMethodBody()?.GetILAsByteArray();
        if (code == null) return new List<CodeOp>();
        return CodeDecoder.Decode(code, token => Member(MetadataTokens.EntityHandle(token)), token =>
        {
            try { return _reader.GetUserString(MetadataTokens.UserStringHandle(token)); }
            catch { return null; }
        });
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
                    return MethodMember(declaring, space, name, reference.DecodeMethodSignature(_names, null), Array.Empty<string>());
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
                        { ValueType = field.DecodeSignature(_names, null), HasThis = (field.Attributes & FieldAttributes.Static) == 0, Namespace = space };
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

    public void Dispose()
    {
        _portable.Dispose();
        _stream.Dispose();
        _context.Dispose();
    }

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
