using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using SpireQoL.Sim;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A branch rule's code fact (SpireQoL.Sim.CodeFact, decision #169) read from a game assembly file's metadata, so the branch checks can read
/// it on both branches' assemblies (each decompile's assembly/sts2.dll, decision #165), as BranchRules.Holds reads it in the running game.
/// </summary>
internal static class CodeFacts
{
    public static bool Holds(string assemblyPath, CodeFact fact)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portable = new PEReader(stream);
        var reader = portable.GetMetadataReader();
        var names = new SignatureNames(reader);
        var typeHandle = reader.TypeDefinitions.FirstOrDefault(handle =>
        {
            var type = reader.GetTypeDefinition(handle);
            return reader.GetString(type.Name) == fact.Type.Name && reader.GetString(type.Namespace) == fact.Type.Namespace;
        });
        if (typeHandle.IsNil) return false;
        var declaring = reader.GetTypeDefinition(typeHandle);
        var methods = declaring.GetMethods().Where(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == fact.Method).ToList();
        if (fact.Mentions == null) return methods.Count > 0;
        if (methods.Count == 0) return false;
        // An async method's body is its state machine's MoveNext ("<Method>d__N", nested in the type).
        var bodies = new List<MethodDefinitionHandle>(methods);
        foreach (var nested in declaring.GetNestedTypes())
        {
            var machine = reader.GetTypeDefinition(nested);
            if (!reader.GetString(machine.Name).StartsWith($"<{fact.Method}>d__", StringComparison.Ordinal)) continue;
            bodies.AddRange(machine.GetMethods().Where(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "MoveNext"));
        }
        foreach (var body in bodies)
        {
            var definition = reader.GetMethodDefinition(body);
            if (definition.RelativeVirtualAddress == 0) continue;
            var code = portable.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes();
            if (code == null) continue;
            if (Sts2Mods.Core.Game.BetaOnly.Tokens(code).Any(token => Names(reader, names, MetadataTokens.EntityHandle(token.Token), fact.Mentions))) return true;
        }
        return false;
    }

    /// <summary>A token names this type or member: itself, its declaring or parent type, or a generic argument.</summary>
    private static bool Names(MetadataReader reader, SignatureNames names, EntityHandle handle, string name)
    {
        bool In(string decoded) => Regex.IsMatch(decoded, $@"(^|[.+<,!]){Regex.Escape(name)}($|[<>,`\[])");
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name) == name;
            case HandleKind.TypeReference:
                return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name) == name;
            case HandleKind.TypeSpecification:
                return In(reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(names, genericContext: null));
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return reader.GetString(method.Name) == name || reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name) == name;
            }
            case HandleKind.FieldDefinition:
            {
                var field = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                return reader.GetString(field.Name) == name || reader.GetString(reader.GetTypeDefinition(field.GetDeclaringType()).Name) == name;
            }
            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                return reader.GetString(member.Name) == name || Names(reader, names, member.Parent, name);
            }
            case HandleKind.MethodSpecification:
            {
                var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return Names(reader, names, specification.Method, name) || specification.DecodeSignature(names, genericContext: null).Any(In);
            }
            default:
                return false;
        }
    }
}
