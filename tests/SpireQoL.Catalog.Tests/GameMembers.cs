using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Every type and member of the game (sts2) a built mod references, checked against a branch's decompiled source, and where each is
/// named (decisions #162, #163): a reference main lacks breaks the whole method that holds it there (a TypeLoadException or
/// MissingMethodException when it's compiled), so it may only be named inside a <see cref="Sts2Mods.Core.Game.BetaOnlyAttribute"/>
/// method, called through its gate. Methods are matched by name and parameter types (simple names), so a changed signature counts as
/// missing; members are looked up through the declared base types.
/// </summary>
internal sealed class GameMembers
{
    /// <summary>Every game reference that branch doesn't declare: what it is.</summary>
    public Dictionary<EntityHandle, string> Missing { get; } = new();

    /// <summary>How many game references were checked.</summary>
    public int Checked { get; private set; }

    /// <summary>A reference the branch lacks named outside a gated method (or in a signature): where, and what.</summary>
    public List<string> Outside { get; } = new();

    /// <summary>Gated methods that name nothing the branch lacks: the gate can come off.</summary>
    public List<string> CaughtUp { get; } = new();

    /// <summary>Gated methods called directly, not through BetaOnly.Gate.</summary>
    public List<string> CalledDirectly { get; } = new();

    /// <summary>The gated methods found.</summary>
    public int Gated { get; private set; }

    private readonly MetadataReader _reader;
    private readonly SignatureNames _names;

    private GameMembers(MetadataReader reader)
    {
        _reader = reader;
        _names = new SignatureNames(reader);
    }

    public static GameMembers Of(string assemblyPath, SourceIndex source)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portable = new PEReader(stream);
        var members = new GameMembers(portable.GetMetadataReader());
        members.FindMissing(source);
        members.FindUses(portable, Path.GetFileName(assemblyPath));
        return members;
    }

    private void FindMissing(SourceIndex source)
    {
        foreach (var handle in _reader.TypeReferences)
        {
            var name = _names.TypeReferenceName(handle, out var assembly);
            if (assembly != SignatureNames.GameAssembly) continue;
            Checked++;
            if (source.Find(name) == null) Missing[handle] = $"type {name}";
        }
        foreach (var handle in _reader.MemberReferences)
        {
            var member = _reader.GetMemberReference(handle);
            string parentName;
            if (member.Parent.Kind == HandleKind.TypeReference)
            {
                parentName = _names.TypeReferenceName((TypeReferenceHandle)member.Parent, out var assembly);
                if (assembly != SignatureNames.GameAssembly) continue;
            }
            else if (member.Parent.Kind == HandleKind.TypeSpecification)
            {
                var decoded = _reader.GetTypeSpecification((TypeSpecificationHandle)member.Parent).DecodeSignature(_names, genericContext: null);
                if (!decoded.StartsWith(SignatureNames.GamePrefix, StringComparison.Ordinal)) continue;
                parentName = decoded;
            }
            else continue;
            parentName = SignatureNames.Plain(parentName);
            var memberName = _reader.GetString(member.Name);
            Checked++;
            if (source.Find(parentName) is not { } type)
            {
                Missing[handle] = $"{parentName}.{memberName} (its type is missing)";
                continue;
            }
            if (member.GetKind() == MemberReferenceKind.Method)
            {
                var parameters = member.DecodeMethodSignature(_names, genericContext: null).ParameterTypes.Select(SignatureNames.Simple).ToList();
                if (!source.HasMethod(type, memberName, parameters))
                    Missing[handle] = $"method {parentName}.{memberName}({string.Join(", ", parameters)}); declared: {source.Describe(type, memberName)}";
            }
            else if (!source.HasField(type, memberName)) Missing[handle] = $"field {parentName}.{memberName}";
        }
    }

    /// <summary>The references a token stands for: itself, its parent type, and the types its signature or instantiation names.</summary>
    private void Expand(EntityHandle handle, HashSet<EntityHandle> into)
    {
        if (!into.Add(handle)) return;
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                var type = _reader.GetTypeReference((TypeReferenceHandle)handle);
                if (type.ResolutionScope.Kind == HandleKind.TypeReference) Expand((EntityHandle)type.ResolutionScope, into);
                break;
            case HandleKind.TypeSpecification:
                Record(into, () => _reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(_names, genericContext: null));
                break;
            case HandleKind.MemberReference:
                var member = _reader.GetMemberReference((MemberReferenceHandle)handle);
                Expand(member.Parent, into);
                Record(into, () => member.GetKind() == MemberReferenceKind.Method
                    ? member.DecodeMethodSignature(_names, genericContext: null).ReturnType
                    : member.DecodeFieldSignature(_names, genericContext: null));
                break;
            case HandleKind.MethodSpecification:
                var specification = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                Expand(specification.Method, into);
                Record(into, () => string.Join(",", specification.DecodeSignature(_names, genericContext: null)));
                break;
        }
    }

    /// <summary>Decodes a signature, adding every type reference it names.</summary>
    private void Record(HashSet<EntityHandle> into, Func<string> decode)
    {
        var seen = new HashSet<TypeReferenceHandle>();
        _names.Seen = seen;
        try { decode(); }
        finally { _names.Seen = null; }
        foreach (var typeHandle in seen) Expand(typeHandle, into);
    }

    private string NameOf(MethodDefinitionHandle handle)
    {
        var method = _reader.GetMethodDefinition(handle);
        var type = _reader.GetTypeDefinition(method.GetDeclaringType());
        return $"{_reader.GetString(type.Name)}.{_reader.GetString(method.Name)}";
    }

    private bool IsGated(MethodDefinitionHandle handle) => _reader.GetMethodDefinition(handle).GetCustomAttributes().Any(attributeHandle =>
    {
        var constructor = _reader.GetCustomAttribute(attributeHandle).Constructor;
        if (constructor.Kind != HandleKind.MethodDefinition) return false;
        var attributeType = _reader.GetTypeDefinition(_reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
        return _reader.GetString(attributeType.Name) == nameof(Sts2Mods.Core.Game.BetaOnlyAttribute);
    });

    private void FindUses(PEReader portable, string file)
    {
        var gated = _reader.MethodDefinitions.Where(IsGated).ToHashSet();
        Gated = gated.Count;
        var usedBy = new Dictionary<EntityHandle, List<string>>();
        void Use(EntityHandle handle, string where)
        {
            if (!Missing.ContainsKey(handle)) return;
            if (!usedBy.TryGetValue(handle, out var places)) usedBy[handle] = places = new();
            places.Add(where);
        }
        foreach (var methodHandle in _reader.MethodDefinitions)
        {
            var method = _reader.GetMethodDefinition(methodHandle);
            var name = NameOf(methodHandle);
            // Its signature loads with every call to it, gated or not.
            var signature = new HashSet<EntityHandle>();
            Record(signature, () => method.DecodeSignature(_names, genericContext: null).ReturnType);
            Record(signature, () => string.Join(",", method.DecodeSignature(_names, genericContext: null).ParameterTypes));
            foreach (var handle in signature) Use(handle, name + " (its signature)");
            if (method.RelativeVirtualAddress == 0) continue;
            var body = portable.GetMethodBody(method.RelativeVirtualAddress);
            var inside = new HashSet<EntityHandle>();
            if (!body.LocalSignature.IsNil)
                Record(inside, () => string.Join(",", _reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(_names, genericContext: null)));
            foreach (var (opCode, token) in Sts2Mods.Core.Game.BetaOnly.Tokens(body.GetILBytes() ?? Array.Empty<byte>()))
            {
                var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(token);
                if (handle.Kind == HandleKind.MethodDefinition && gated.Contains((MethodDefinitionHandle)handle) && !gated.Contains(methodHandle)
                    && opCode.Value != System.Reflection.Emit.OpCodes.Ldftn.Value)
                    CalledDirectly.Add($"{name} calls {NameOf((MethodDefinitionHandle)handle)} [{file}]");
                Expand(handle, inside);
            }
            if (gated.Contains(methodHandle))
            {
                if (!inside.Any(Missing.ContainsKey)) CaughtUp.Add($"{name} [{file}]");
            }
            else foreach (var handle in inside) Use(handle, name);
        }
        foreach (var fieldHandle in _reader.FieldDefinitions)
        {
            var field = _reader.GetFieldDefinition(fieldHandle);
            var signature = new HashSet<EntityHandle>();
            Record(signature, () => field.DecodeSignature(_names, genericContext: null));
            var owner = _reader.GetTypeDefinition(field.GetDeclaringType());
            foreach (var handle in signature) Use(handle, $"{_reader.GetString(owner.Name)}.{_reader.GetString(field.Name)} (a field)");
        }
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var type = _reader.GetTypeDefinition(typeHandle);
            var bases = new HashSet<EntityHandle>();
            if (!type.BaseType.IsNil) Expand(type.BaseType, bases);
            foreach (var interfaceHandle in type.GetInterfaceImplementations()) Expand(_reader.GetInterfaceImplementation(interfaceHandle).Interface, bases);
            foreach (var handle in bases) Use(handle, $"{_reader.GetString(type.Name)} (its base types)");
        }
        foreach (var (handle, places) in usedBy) Outside.Add($"{Missing[handle]} [{file}], named in {string.Join(", ", places.Distinct().Take(4))}");
    }
}

/// <summary>Type names from metadata signatures: the game's types marked with "sts2!", generic arguments in angle brackets.</summary>
internal sealed class SignatureNames : ISignatureTypeProvider<string, object?>
{
    public const string GameAssembly = "sts2";
    public const string GamePrefix = GameAssembly + "!";

    private readonly MetadataReader _reader;

    public SignatureNames(MetadataReader reader) => _reader = reader;

    /// <summary>While set, every type reference a decode names is added to it.</summary>
    public HashSet<TypeReferenceHandle>? Seen { get; set; }

    public string TypeReferenceName(TypeReferenceHandle handle, out string assembly)
    {
        var type = _reader.GetTypeReference(handle);
        var name = _reader.GetString(type.Name);
        var space = _reader.GetString(type.Namespace);
        switch (type.ResolutionScope.Kind)
        {
            case HandleKind.TypeReference:
                return TypeReferenceName((TypeReferenceHandle)type.ResolutionScope, out assembly) + "+" + name;
            case HandleKind.AssemblyReference:
                assembly = _reader.GetString(_reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name);
                break;
            default:
                assembly = "";
                break;
        }
        return space.Length > 0 ? space + "." + name : name;
    }

    /// <summary>"sts2!A.B`1&lt;x&gt;" as "A.B`1".</summary>
    public static string Plain(string name)
    {
        if (name.StartsWith(GamePrefix, StringComparison.Ordinal)) name = name[GamePrefix.Length..];
        var angle = name.IndexOf('<');
        return angle >= 0 ? name[..angle] : name;
    }

    /// <summary>A parameter type's simple name, as the source writes it: no namespace, arity or generic arguments; ref dropped.</summary>
    public static string Simple(string name)
    {
        var suffix = "";
        while (name.EndsWith("[]", StringComparison.Ordinal) || name.EndsWith('&') || name.EndsWith('*'))
        {
            var cut = name.EndsWith("[]", StringComparison.Ordinal) ? 2 : 1;
            suffix = name[^cut..] + suffix;
            name = name[..^cut];
        }
        var plain = name;
        var angle = plain.IndexOf('<');
        if (angle >= 0) plain = plain[..angle];
        if (plain.StartsWith(GamePrefix, StringComparison.Ordinal)) plain = plain[GamePrefix.Length..];
        var tick = plain.IndexOf('`');
        if (tick >= 0) plain = plain[..tick];
        var dot = Math.Max(plain.LastIndexOf('.'), plain.LastIndexOf('+'));
        if (dot >= 0) plain = plain[(dot + 1)..];
        return SourceIndex.Keyword(plain) + suffix.Replace("&", "");
    }

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        Seen?.Add(handle);
        var name = TypeReferenceName(handle, out var assembly);
        return assembly == GameAssembly ? GamePrefix + name : name;
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

/// <summary>One type declared in the decompiled source: its base types (simple names) and members.</summary>
internal sealed class DeclaredType
{
    public required string FullName { get; init; }
    public List<string> Bases { get; } = new();
    public bool IsDelegate { get; set; }
    public bool IsEnum { get; set; }
    public bool IsRecord { get; set; }
    public List<(string Name, List<string> Parameters)> Methods { get; } = new();
    public HashSet<string> Fields { get; } = new();
}

/// <summary>The types and members a branch's decompiled source declares (parsed with Roslyn, no compile).</summary>
internal sealed class SourceIndex
{
    private readonly Dictionary<string, DeclaredType> _types = new();
    private readonly Dictionary<string, List<DeclaredType>> _bySimpleName = new();

    public int Count => _types.Count;

    public static string Keyword(string name) => name switch
    {
        "Int32" => "int", "Int64" => "long", "Boolean" => "bool", "String" => "string", "Single" => "float", "Double" => "double",
        "Decimal" => "decimal", "Object" => "object", "Void" => "void", "Byte" => "byte", "Char" => "char", "UInt32" => "uint",
        "UInt64" => "ulong", "Int16" => "short", "UInt16" => "ushort", "SByte" => "sbyte", "IntPtr" => "nint", "UIntPtr" => "nuint",
        _ => name,
    };

    public static SourceIndex Build(string root)
    {
        var index = new SourceIndex();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + ".git")) continue;
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var nodes = tree.GetRoot().DescendantNodes().ToList();
            foreach (var declaration in nodes.OfType<BaseTypeDeclarationSyntax>()) index.Add(declaration);
            foreach (var declaration in nodes.OfType<DelegateDeclarationSyntax>()) index.Get(NameOf(declaration, declaration.Identifier.Text, declaration.TypeParameterList)).IsDelegate = true;
        }
        return index;
    }

    private static string NameOf(SyntaxNode node, string identifier, TypeParameterListSyntax? typeParameters)
    {
        var name = identifier + (typeParameters is { Parameters.Count: > 0 } list ? "`" + list.Parameters.Count : "");
        if (node.Parent is BaseTypeDeclarationSyntax outer)
            return NameOf(outer, outer.Identifier.Text, (outer as TypeDeclarationSyntax)?.TypeParameterList) + "+" + name;
        var spaces = new List<string>();
        for (var current = node.Parent; current != null; current = current.Parent)
            if (current is BaseNamespaceDeclarationSyntax space) spaces.Insert(0, space.Name.ToString());
        return spaces.Count > 0 ? string.Join(".", spaces) + "." + name : name;
    }

    private DeclaredType Get(string fullName)
    {
        if (_types.TryGetValue(fullName, out var type)) return type;
        type = new DeclaredType { FullName = fullName };
        _types[fullName] = type;
        var simple = fullName[(Math.Max(fullName.LastIndexOf('.'), fullName.LastIndexOf('+')) + 1)..];
        var tick = simple.IndexOf('`');
        if (tick >= 0) simple = simple[..tick];
        if (!_bySimpleName.TryGetValue(simple, out var list)) _bySimpleName[simple] = list = new();
        list.Add(type);
        return type;
    }

    private void Add(BaseTypeDeclarationSyntax declaration)
    {
        var type = Get(NameOf(declaration, declaration.Identifier.Text, (declaration as TypeDeclarationSyntax)?.TypeParameterList));
        type.IsEnum = declaration is EnumDeclarationSyntax;
        type.IsRecord = declaration is RecordDeclarationSyntax;
        if (declaration.BaseList != null) foreach (var baseType in declaration.BaseList.Types) type.Bases.Add(TypeName(baseType.Type));
        if (declaration is RecordDeclarationSyntax { ParameterList: { } recordParameters })
        {
            type.Methods.Add((".ctor", recordParameters.Parameters.Select(parameter => TypeName(parameter.Type!)).ToList()));
            foreach (var parameter in recordParameters.Parameters)
            {
                type.Methods.Add(("get_" + parameter.Identifier.Text, new()));
                type.Methods.Add(("set_" + parameter.Identifier.Text, new() { TypeName(parameter.Type!) }));
            }
        }
        if (declaration is not TypeDeclarationSyntax typeDeclaration) return;
        foreach (var member in typeDeclaration.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    type.Methods.Add((method.Identifier.Text, Parameters(method.ParameterList)));
                    break;
                case ConstructorDeclarationSyntax constructor:
                    type.Methods.Add((constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor", Parameters(constructor.ParameterList)));
                    break;
                case OperatorDeclarationSyntax operatorDeclaration:
                    type.Methods.Add(("op_" + operatorDeclaration.OperatorToken.Text, Parameters(operatorDeclaration.ParameterList)));
                    break;
                case ConversionOperatorDeclarationSyntax conversion:
                    type.Methods.Add((conversion.ImplicitOrExplicitKeyword.Text == "implicit" ? "op_Implicit" : "op_Explicit", Parameters(conversion.ParameterList)));
                    break;
                case PropertyDeclarationSyntax property:
                    type.Methods.Add(("get_" + property.Identifier.Text, new()));
                    type.Methods.Add(("set_" + property.Identifier.Text, new() { TypeName(property.Type) }));
                    type.Fields.Add(property.Identifier.Text);
                    break;
                case IndexerDeclarationSyntax indexer:
                    var indexParameters = indexer.ParameterList.Parameters.Select(parameter => TypeName(parameter.Type!)).ToList();
                    type.Methods.Add(("get_Item", indexParameters));
                    type.Methods.Add(("set_Item", indexParameters.Append(TypeName(indexer.Type)).ToList()));
                    break;
                case EventFieldDeclarationSyntax eventField:
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        type.Methods.Add(("add_" + variable.Identifier.Text, new() { TypeName(eventField.Declaration.Type) }));
                        type.Methods.Add(("remove_" + variable.Identifier.Text, new() { TypeName(eventField.Declaration.Type) }));
                        type.Fields.Add(variable.Identifier.Text);
                    }
                    break;
                case EventDeclarationSyntax eventDeclaration:
                    type.Methods.Add(("add_" + eventDeclaration.Identifier.Text, new() { TypeName(eventDeclaration.Type) }));
                    type.Methods.Add(("remove_" + eventDeclaration.Identifier.Text, new() { TypeName(eventDeclaration.Type) }));
                    break;
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables) type.Fields.Add(variable.Identifier.Text);
                    break;
            }
        }
    }

    private static List<string> Parameters(ParameterListSyntax list) => list.Parameters.Select(parameter => TypeName(parameter.Type!)).ToList();

    /// <summary>A type as the metadata names it, simply: int? is Nullable, a reference type's ? is nothing, a tuple is ValueTuple.</summary>
    private static string TypeName(TypeSyntax type) => type switch
    {
        NullableTypeSyntax { ElementType: PredefinedTypeSyntax { Keyword.Text: not ("string" or "object") } } => "Nullable",
        NullableTypeSyntax nullable => TypeName(nullable.ElementType),
        ArrayTypeSyntax array => TypeName(array.ElementType) + "[]",
        PointerTypeSyntax pointer => TypeName(pointer.ElementType) + "*",
        RefTypeSyntax reference => TypeName(reference.Type),
        TupleTypeSyntax => "ValueTuple",
        PredefinedTypeSyntax predefined => predefined.Keyword.Text,
        QualifiedNameSyntax qualified => TypeName(qualified.Right),
        AliasQualifiedNameSyntax alias => TypeName(alias.Name),
        GenericNameSyntax generic => generic.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        _ => type.ToString(),
    };

    public DeclaredType? Find(string fullName) => _types.GetValueOrDefault(SignatureNames.Plain(fullName));

    private IEnumerable<DeclaredType> WithBases(DeclaredType type)
    {
        var seen = new HashSet<DeclaredType>();
        var queue = new Queue<DeclaredType>();
        queue.Enqueue(type);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            yield return current;
            foreach (var baseName in current.Bases)
                if (_bySimpleName.TryGetValue(baseName, out var candidates))
                    foreach (var candidate in candidates) queue.Enqueue(candidate);
        }
    }

    /// <summary>A generic parameter in the reference ("!0", "!!1") matches anything; a declared type parameter (T, TModel) too.</summary>
    private static bool SameType(string referenced, string declared) =>
        referenced.StartsWith('!') || referenced == declared
        || (declared.Length > 0 && char.IsUpper(declared[0]) && (declared.Length == 1 || (declared[0] == 'T' && char.IsUpper(declared[1]))));

    public bool HasMethod(DeclaredType type, string name, List<string> parameters)
    {
        if (type.IsDelegate || type.IsEnum) return true;
        if (name == ".ctor" && parameters.Count == 0 && !type.Methods.Any(method => method.Name == ".ctor")) return true; // the implicit constructor
        if (WithBases(type).Any(current => current.Methods.Any(method => method.Name == name && method.Parameters.Count == parameters.Count
            && method.Parameters.Zip(parameters).All(pair => SameType(pair.Second, pair.First))))) return true;
        // What the compiler makes for a record.
        return type.IsRecord && (name.StartsWith('<') || name is "Equals" or "GetHashCode" or "ToString" or "op_Equality" or "op_Inequality" or "PrintMembers" or "get_EqualityContract");
    }

    public bool HasField(DeclaredType type, string name) => type.IsEnum || WithBases(type).Any(current => current.Fields.Contains(name));

    public string Describe(DeclaredType type, string name)
    {
        var declared = WithBases(type).SelectMany(current => current.Methods.Where(method => method.Name == name)
            .Select(method => $"{current.FullName}.{method.Name}({string.Join(", ", method.Parameters)})")).ToList();
        return declared.Count == 0 ? "none" : string.Join(" | ", declared);
    }
}
