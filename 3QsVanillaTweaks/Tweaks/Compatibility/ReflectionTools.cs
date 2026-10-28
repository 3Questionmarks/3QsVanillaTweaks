using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ThreeQsVanillaTweaks
{
    // Optional compatibility probes are normal. Required hook sites still validate their result.
    internal static class ReflectionTools
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        internal static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, Declared); if (field != null) return field; }
            return null;
        }
        internal static PropertyInfo Property(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperties(Declared).FirstOrDefault(p => p.Name == name); if (property != null) return property;
            }
            return null;
        }
        internal static MethodInfo Method(Type type, string name, Type[] parameters = null, Type[] generics = null)
        {
            for (; type != null; type = type.BaseType)
            {
                MethodInfo method = DeclaredMethod(type, name, parameters, generics); if (method != null) return method;
            }
            return null;
        }
        internal static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters = null, Type[] generics = null)
        {
            if (type == null) return null;
            MethodInfo[] candidates = type.GetMethods(Declared).Where(m => m.Name == name).ToArray();
            if (generics != null) candidates = candidates.Where(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == generics.Length).Select(m => m.MakeGenericMethod(generics)).ToArray();
            if (parameters != null) return candidates.FirstOrDefault(m => m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
            return candidates.FirstOrDefault(m => m.GetParameters().Length == 0) ?? candidates.FirstOrDefault();
        }
        internal static List<MethodInfo> GetDeclaredMethods(Type type) => type == null ? new List<MethodInfo>() : type.GetMethods(Declared).ToList();
        internal static List<FieldInfo> GetDeclaredFields(Type type) => type == null ? new List<FieldInfo>() : type.GetFields(Declared).ToList();
        internal static Type TypeByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Type found = Type.GetType(name, false); if (found != null) return found;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) { found = assembly.GetType(name, false); if (found != null) return found; }
            return null;
        }
    }
}
