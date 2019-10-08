﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CS2X.Core.Transpilers
{
	public abstract class Transpiler
	{
		public readonly Solution solution;
		protected INamedTypeSymbol runtimeType, typeType, arrayType, objectType;
		protected INamedTypeSymbol stringType, stringBuilderType;
		protected INamedTypeSymbol delegateType, multicastDelegateType;
		protected INamedTypeSymbol ienumerableT, ienumerable, ienumeratorT, ienumerator;
		protected IMethodSymbol ienumerableT_GetEnumerator, ienumerable_GetEnumerator, ienumeratorT_GetEnumerator, ienumerator_GetEnumerator;

		public Transpiler(Solution solution)
		{
			this.solution = solution;
			var coreLibProject = solution.coreLibProject;

			// get common types
			runtimeType = coreLibProject.compilation.GetTypeByMetadataName("System.RuntimeType");
			typeType = coreLibProject.compilation.GetTypeByMetadataName("System.Type");

			arrayType = coreLibProject.compilation.GetSpecialType(SpecialType.System_Array);
			objectType = coreLibProject.compilation.GetSpecialType(SpecialType.System_Object);

			stringType = coreLibProject.compilation.GetSpecialType(SpecialType.System_String);
			stringBuilderType = coreLibProject.compilation.GetTypeByMetadataName("System.Text.StringBuilder");

			delegateType = coreLibProject.compilation.GetSpecialType(SpecialType.System_Delegate);
			multicastDelegateType = coreLibProject.compilation.GetSpecialType(SpecialType.System_MulticastDelegate);

			// get common generic types
			ienumerableT = coreLibProject.compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
			ienumerable = coreLibProject.compilation.GetSpecialType(SpecialType.System_Collections_IEnumerable);
			ienumeratorT = coreLibProject.compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerator_T);
			ienumerator = coreLibProject.compilation.GetSpecialType(SpecialType.System_Collections_IEnumerator);
			ienumerableT_GetEnumerator = FindMethodByName(ienumerableT, "GetEnumerator");
			ienumerable_GetEnumerator = FindMethodByName(ienumerable, "GetEnumerator");
			ienumeratorT_GetEnumerator = FindMethodByName(ienumeratorT, "get_Current");
			ienumerator_GetEnumerator = FindMethodByName(ienumerator, "get_Current");
		}

		public abstract void Transpile(string outputPath);

		protected CSharpCompilation GetCompilation(IAssemblySymbol assembly, Solution solution)
		{
			foreach (var project in solution.projects)
			{
				if (assembly.Equals(project.compilation.Assembly)) return project.compilation;
			}
			throw new Exception("Failed to find CSharpCompilation for assembly: " + assembly.Name);
		}

		protected int NestedCount(ITypeSymbol type)
		{
			int count = 0;

			// count nested types
			var lastType = type;
			type = type.ContainingType;
			while (type != null)
			{
				lastType = type;
				type = type.ContainingType;
				++count;
			}

			// count nested namespaces
			var nestedNamespace = lastType.ContainingNamespace;
			while (nestedNamespace != null)
			{
				nestedNamespace = nestedNamespace.ContainingNamespace;
				++count;
			}

			return count;
		}

		protected int NestedCount(IMethodSymbol method)
		{
			return NestedCount(method.ContainingType);
		}

		protected virtual string GetTypeFullName(ITypeSymbol type)
		{
			var result = new StringBuilder(type.Name);

			// prefix containing types
			var containingType = type.ContainingType;
			while (containingType != null)
			{
				result.Insert(0, containingType.Name + GetContainingTypeDelimiter());
				containingType = containingType.ContainingType;
			}

			// write namespace
			var baseNamespace = type.ContainingNamespace;
			while (baseNamespace != null)
			{
				if (baseNamespace.IsGlobalNamespace) break;
				result.Insert(0, baseNamespace.Name + GetNamespaceDelimiter());
				baseNamespace = baseNamespace.ContainingNamespace;
			}

			return result.ToString();
		}

		protected virtual string GetFieldFullName(IFieldSymbol field)
		{
			string result = field.Name;
			ParseImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetParameterFullName(IParameterSymbol parameter)
		{
			string result = parameter.Name;
			ParseImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetLocalFullName(ILocalSymbol local)
		{
			string result = local.Name;
			ParseImplementationDetail(ref result);
			return result;
		}

		protected virtual string GetMethodFullName(IMethodSymbol method)
		{
			string result = method.Name;
			ParseImplementationDetail(ref result);
			return result;
		}

		protected abstract string GetContainingTypeDelimiter();
		protected abstract string GetNamespaceDelimiter();

		protected void ParseImplementationDetail(ref string name)
		{
			if (name.Contains('<')) name = name.Replace('<', '_');
			if (name.Contains('>')) name = name.Replace('>', '_');
			if (name.Contains('[')) name = name.Replace('[', '_');
			if (name.Contains(']')) name = name.Replace(']', '_');
			if (name.Contains('.')) name = name.Replace('.', '_');
			if (name.Contains('*')) name = name.Replace('*', '_');
		}

		protected bool IsAutoProperty(IPropertySymbol property) { return IsAutoProperty(property, out _); }
		protected bool IsAutoProperty(IPropertySymbol property, out IFieldSymbol field)
		{
			var members = property.ContainingType.GetMembers();
			field = (IFieldSymbol)members.FirstOrDefault(x => x.Kind == SymbolKind.Field && property.Equals(((IFieldSymbol)x).AssociatedSymbol));
			return field != null;
		}

		protected bool IsAutoPropertyMethod(IMethodSymbol method) { return IsAutoPropertyMethod(method, out _, out _); }
		protected bool IsAutoPropertyMethod(IMethodSymbol method, out IPropertySymbol property, out IFieldSymbol field)
		{
			field = null;
			if (method.MethodKind != MethodKind.PropertyGet && method.MethodKind != MethodKind.PropertySet)
			{
				property = null;
				return false;
			}
			property = method.AssociatedSymbol as IPropertySymbol;
			return property != null && IsAutoProperty(property, out field);
		}

		protected bool IsEmptyType(ITypeSymbol type, bool staticsDontCount = true)
		{
			var currentType = type;
			if (staticsDontCount)
			{
				while (currentType != null)
				{
					foreach (var member in currentType.GetMembers())
					{
						if (!(member is IFieldSymbol) || member.IsStatic) continue;
						return false;
					}
					currentType = currentType.BaseType;
				}
			}
			else
			{
				while (currentType != null)
				{
					if (currentType.GetMembers().Any(x => x is IFieldSymbol)) return false;
					currentType = currentType.BaseType;
				}
			}

			return true;
		}

		protected bool IsAtomicType(ITypeSymbol type)
		{
			if (type.Kind == SymbolKind.ArrayType)
			{
				var array = (IArrayTypeSymbol)type;
				if (array.ElementType.IsReferenceType) return false;
			}

			var currentType = type;
			while (currentType != null)
			{
				foreach (var member in currentType.GetMembers())
				{
					if (!(member is IFieldSymbol) || member.IsStatic || ((IFieldSymbol)member).Type.IsValueType) continue;
					return false;
				}
				currentType = currentType.BaseType;
			}

			return true;
		}

		protected bool HasFields(ITypeSymbol type)
		{
			return type.GetMembers().Any(x => x is IFieldSymbol);
		}

		protected bool HasProperties(ITypeSymbol type)
		{
			return type.GetMembers().Any(x => x is IPropertySymbol);
		}

		protected bool HasMethods(ITypeSymbol type)
		{
			return type.GetMembers().Any(x => x is IMethodSymbol);
		}

		protected bool IsPrimitiveType(ITypeSymbol type)
		{
			switch (type.SpecialType)
			{
				case SpecialType.None: return false;
				case SpecialType.System_Boolean:
				case SpecialType.System_Char:
				case SpecialType.System_SByte:
				case SpecialType.System_Byte: 
				case SpecialType.System_Int16:
				case SpecialType.System_UInt16:
				case SpecialType.System_Int32:
				case SpecialType.System_UInt32:
				case SpecialType.System_Int64:
				case SpecialType.System_UInt64:
				case SpecialType.System_Single:
				case SpecialType.System_Double:
				case SpecialType.System_IntPtr:
				case SpecialType.System_UIntPtr:
					return true;
			}
			return false;
		}

		protected int GetBaseTypeCount(INamedTypeSymbol type)
		{
			int count = 0;
			var baseType = type.BaseType;
			while (baseType != null)
			{
				++count;
				baseType = baseType.BaseType;
			}
			return count;
		}

		protected int GetMethodOverloadIndex(IMethodSymbol method)
		{
			int index = 0;
			foreach (var member in method.ContainingType.GetMembers())
			{
				if (member.Kind == SymbolKind.Method)
				{
					var otherMethod = (IMethodSymbol)member;
					if (otherMethod.Equals(method) || (method.IsExtensionMethod && otherMethod.Equals(method.ReducedFrom))) break;
					else if (otherMethod.Name == method.Name) ++index;
				}
			}
			return index;
		}

		protected int GetVirtualMethodOverloadIndex(IMethodSymbol method)
		{
			if (!IsVirtualMethod(method)) throw new Exception("Method must be virtual: " + method.FullName());
			if (method.OverriddenMethod != null)
			{
				return GetVirtualMethodOverloadIndex(method.OverriddenMethod);
			}
			else
			{
				bool found = false;
				int index = 0;
				foreach (var member in method.ContainingType.GetMembers())
				{
					var memberMethod = member as IMethodSymbol;
					if (memberMethod == null) continue;
					if (method.Equals(memberMethod) || (method.IsGenericMethod && method.ConstructedFrom.Equals(memberMethod)))
					{
						found = true;
						break;
					}
					if (method.Name == memberMethod.Name) ++index;
				}

				if (!found) throw new Exception("Failed to find virtual method index (this should never happen)");
				return index;
			}
		}

		protected List<IMethodSymbol> GetOrderedVirtualMethods(ITypeSymbol type)
		{
			var virtualMethodList = new List<IMethodSymbol>();
			void AddVirtualMethods(ITypeSymbol namedType)
			{
				foreach (IMethodSymbol method in namedType.GetMembers().Where(x => x.Kind == SymbolKind.Method).Reverse())
				{
					if ((!method.IsVirtual && !method.IsAbstract) || method.IsOverride) continue;
					virtualMethodList.Insert(0, method);
				}
			}

			var baseType = type;
			do
			{
				AddVirtualMethods(baseType);
				baseType = baseType.BaseType;
			} while (baseType != null);

			return virtualMethodList;
		}

		protected IMethodSymbol FindHighestVirtualMethodSlot(ITypeSymbol type, IMethodSymbol rootSlotMethod)
		{
			var baseType = type;
			do
			{
				foreach (var member in baseType.GetMembers())
				{
					if (member.Kind != SymbolKind.Method) continue;
					var method = (IMethodSymbol)member;
					if (!IsVirtualMethod(method)) continue;
					if (method.Equals(rootSlotMethod) || rootSlotMethod.Equals(method.OverriddenMethod)) return method;
					if (rootSlotMethod.IsGenericMethod && (rootSlotMethod.ConstructedFrom.Equals(method) || rootSlotMethod.ConstructedFrom.Equals(method.OverriddenMethod))) return method;
				}
				baseType = baseType.BaseType;
			} while (baseType != null);
			throw new Exception("Failed to find highest virtual method slot");
		}

		protected bool IsVirtualMethod(IMethodSymbol method)
		{
			return method.IsOverride || method.IsVirtual || method.IsAbstract;
		}

		protected bool IsInternalCall(IMethodSymbol method)
		{
			foreach (var a in method.GetAttributes())
			{
				if
				(
					a.AttributeClass.ContainingNamespace != null && a.AttributeClass.ContainingNamespace.Name == "CompilerServices" &&
					a.AttributeClass.ContainingNamespace.ContainingNamespace != null && a.AttributeClass.ContainingNamespace.ContainingNamespace.Name == "Runtime" &&
					a.AttributeClass.ContainingNamespace.ContainingNamespace.ContainingNamespace != null && a.AttributeClass.ContainingNamespace.ContainingNamespace.ContainingNamespace.Name == "System" &&
					a.AttributeClass.Name == "MethodImplAttribute"
				)
				{
					if ((int)a.ConstructorArguments[0].Value == 0x1000) return true;
				}
			}
			return false;
		}

		protected bool IsCS2XAttributeType(ITypeSymbol type)
		{
			return type.Name == "NativeTarget" || type.Name == "NativeExternAttribute" || type.Name == "NativeTypeAttribute";
		}

		protected bool GetNativeExternName(IMethodSymbol method, NativeTarget target, out string name)
		{
			if (GetNativeExternAttribute(method, target, out var nativeExternAttribute))
			{
				var constant = nativeExternAttribute.ConstructorArguments[1];
				if (constant.Value == null) name = method.Name;
				else name = constant.Value.ToString();
				return true;
			}

			name = null;
			return false;
		}

		protected bool GetDllImportName(IMethodSymbol method, out string dllName, out string name)
		{
			if (GetDllImportAttribute(method, out var dllImportAttribute))
			{
				dllName = (string)dllImportAttribute.ConstructorArguments[0].Value;
				string entryPoint = null;
				if (dllImportAttribute.NamedArguments.Any(x => x.Key == "EntryPoint"))
				{
					entryPoint = dllImportAttribute.NamedArguments.First(x => x.Key == "EntryPoint").Value.Value as string;
				}
				if (entryPoint == null) entryPoint = method.Name;

				name = entryPoint;
				return true;
			}

			dllName = null;
			name = null;
			return false;
		}

		protected bool GetNativeTypeName(ITypeSymbol type, NativeTarget target, out string name)
		{
			if (GetNativeTypeAttribute(type, target, out var attribute))
			{
				var constant = attribute.ConstructorArguments[1];
				if (constant.Value == null) name = type.Name;
				else name = constant.Value.ToString();
				return true;
			}

			name = null;
			return false;
		}

		protected bool GetNativeExternAttribute(IMethodSymbol method, NativeTarget target, out AttributeData attribute)
		{
			return GetCS2XAttribute(method, target, "NativeExternAttribute", out attribute);
		}

		protected bool GetNativeTypeAttribute(ITypeSymbol type, NativeTarget target, out AttributeData attribute)
		{
			return GetCS2XAttribute(type, target, "NativeTypeAttribute", out attribute);
		}

		protected bool GetCS2XAttribute(ISymbol symbol, NativeTarget target, string attributeTypeName, out AttributeData attribute)
		{
			foreach (var a in symbol.GetAttributes())
			{
				var type = a.AttributeClass;
				if (type.ContainingNamespace.Name == "CS2X" && type.Name == attributeTypeName)
				{
					if (!a.ConstructorArguments.Any(x => ((int)x.Value & 1) == (int)target)) throw new NotImplementedException($"NativeTarget not set for '{target}': " + symbol.FullName());
					attribute = a;
					return true;
				}
			}

			attribute = null;
			return false;
		}

		protected bool GetDllImportAttribute(IMethodSymbol method, out AttributeData attribute)
		{
			attribute = FindAttributeByName(method, "System.Runtime.InteropServices.DllImportAttribute");
			return attribute != null;
		}

		protected AttributeData FindAttributeByName(ISymbol symbol, string attributeTypeFullName)
		{
			foreach (var attribute in symbol.GetAttributes())
			{
				var type = attribute.AttributeClass;
				if (type.FullName() == attributeTypeFullName)
				{
					return attribute;
				}
			}
			return null;
		}

		protected IFieldSymbol FindFieldByName(ITypeSymbol type, string fieldName)
		{
			return type.GetMembers().FirstOrDefault(x => x.Kind == SymbolKind.Field && x.Name == fieldName) as IFieldSymbol;
		}

		protected IPropertySymbol FindPropertyByName(ITypeSymbol type, string propertyName)
		{
			return type.GetMembers().FirstOrDefault(x => x.Kind == SymbolKind.Property && x.Name == propertyName) as IPropertySymbol;
		}

		protected IFieldSymbol FindAutoPropertyFieldByName(ITypeSymbol type, string propertyName)
		{
			var members = type.GetMembers();
			var property = members.FirstOrDefault(x => x.Kind == SymbolKind.Property && x.Name == propertyName) as IPropertySymbol;
			if (property == null) return null;
			return members.FirstOrDefault(x => x.Kind == SymbolKind.Field && property.Equals(((IFieldSymbol)x).AssociatedSymbol)) as IFieldSymbol;
		}

		protected IMethodSymbol FindMethodByName(ITypeSymbol type, string methodName)
		{
			return type.GetMembers().FirstOrDefault(x => x.Kind == SymbolKind.Method && x.Name == methodName) as IMethodSymbol;
		}

		protected IMethodSymbol FindMethodByReturnType(ITypeSymbol type, string methodName, ITypeSymbol returnType)
		{
			return type.GetMembers().FirstOrDefault(x => x.Kind == SymbolKind.Method && x.Name == methodName && ((IMethodSymbol)x).ReturnType.Equals(returnType)) as IMethodSymbol;
		}

		protected IMethodSymbol FindMethodBySignature(ITypeSymbol type, string methodName, ITypeSymbol returnType, params ITypeSymbol[] parameters)
		{
			foreach (var member in type.GetMembers())
			{
				if (member.Kind != SymbolKind.Method) continue;
				if (member.Name != methodName) continue;
				var method = (IMethodSymbol)member;
				if (!method.ReturnType.Equals(returnType)) continue;
				if (method.Parameters.Length != parameters.Length) continue;

				bool match = true;
				for (int i = 0; i != method.Parameters.Length; ++i)
				{
					if (!method.Parameters[i].Type.Equals(parameters[i]))
					{
						match = false;
						break;
					}
				}
				if (match) return method;
			}
			return null;
		}

		protected bool FindDestructor(ITypeSymbol type, out IMethodSymbol destructor)
		{
			foreach (var member in type.GetMembers())
			{
				if (member.Kind != SymbolKind.Method) continue;
				var method = (IMethodSymbol)member;
				if (method.MethodKind != MethodKind.Destructor || method.Parameters.Length != 0) continue;
				destructor = method;
				return true;
			}
			destructor = null;
			return false;
		}

		protected IMethodSymbol FindDefaultConstructor(ITypeSymbol type)
		{
			foreach (var member in type.GetMembers())
			{
				if (member.Kind != SymbolKind.Method) continue;
				var method = (IMethodSymbol)member;
				if (method.MethodKind != MethodKind.Constructor || method.Parameters.Length != 0) continue;
				return method;
			}
			throw new Exception("Failed to find default constructor");
		}

		protected IMethodSymbol FindDelegateConstructor(ITypeSymbol type)
		{
			foreach (var member in type.GetMembers())
			{
				if (member.Kind != SymbolKind.Method) continue;
				var method = (IMethodSymbol)member;
				var ps = method.Parameters;
				if (method.MethodKind != MethodKind.Constructor || ps.Length != 2 || ps[0].Type.SpecialType != SpecialType.System_Object || ps[1].Type.SpecialType != SpecialType.System_IntPtr) continue;
				return method;
			}
			throw new Exception("Failed to find delegate constructor");
		}

		protected string GetProjectNameFlat(Project project)
		{
			string refAssemblyName = project.roslynProject.AssemblyName;
			ParseImplementationDetail(ref refAssemblyName);
			return refAssemblyName;
		}

		protected ITypeSymbol ResolveType(ITypeSymbol type, IMethodSymbol usedWithinMethod, SemanticModel semanticModel)
		{
			if (type.Kind == SymbolKind.NamedType)
			{
				var symbol = (INamedTypeSymbol)type;

				// check if we need to resolve generic
				if (!symbol.IsGenericType) return symbol;

				// if nested generic, resolve containing type and find resolved symbol
				if (symbol.TypeArguments.Length == 0)
				{
					if (symbol.ContainingType == null) throw new NotSupportedException("Unsupported generic type: " + type.FullName());
					var containingType = (INamedTypeSymbol)ResolveType(symbol.ContainingType, usedWithinMethod, semanticModel);
					symbol = (INamedTypeSymbol)containingType.GetMembers().First(x => x.OriginalDefinition.Equals(symbol.OriginalDefinition));
				}

				// check if all generic type args are resolved
				if (!symbol.TypeArguments.Any(x => x.TypeKind == TypeKind.TypeParameter)) return symbol;

				// resolve generic parameters
				var parameters = symbol.TypeArguments;
				var typeParams = new ITypeSymbol[parameters.Length];
				for (int i = 0; i != typeParams.Length; ++i) typeParams[i] = ResolveType(parameters[i], usedWithinMethod, semanticModel);

				// construct from generic source
				return symbol.OriginalDefinition.Construct(typeParams);
			}
			else if (type.Kind == SymbolKind.ArrayType)
			{
				var symbol = (IArrayTypeSymbol)type;
				var resolvedType = ResolveType(symbol.ElementType, usedWithinMethod, semanticModel);
				return semanticModel.Compilation.CreateArrayTypeSymbol(resolvedType);
			}
			else if (type.Kind == SymbolKind.PointerType)
			{
				var symbol = (IPointerTypeSymbol)type;
				var resolvedType = ResolveType(symbol.PointedAtType, usedWithinMethod, semanticModel);
				return semanticModel.Compilation.CreatePointerTypeSymbol(resolvedType);
			}
			else if (type.Kind == SymbolKind.TypeParameter)
			{
				var symbol = (ITypeParameterSymbol)type;
				if (symbol.TypeParameterKind == TypeParameterKind.Type)
				{
					var containingType = usedWithinMethod.ContainingType;
					while (containingType != null)
					{
						if (containingType.OriginalDefinition.Equals(symbol.ContainingType.OriginalDefinition)) break;
						containingType = containingType.ContainingType;
					}
					if (containingType == null) throw new Exception("Failed to find ITypeParameterSymbol type: " + symbol.Name);
					return containingType.TypeArguments[symbol.Ordinal];
				}
				else if (symbol.TypeParameterKind == TypeParameterKind.Method)
				{
					return usedWithinMethod.TypeArguments[symbol.Ordinal];
				}
				else
				{
					throw new NotSupportedException("Unsupported ITypeParameterSymbol kind: " + symbol.TypeParameterKind);
				}
			}

			return type;
		}

		protected IMethodSymbol ResolveMethod(IMethodSymbol method, IMethodSymbol usedWithinMethod, SemanticModel semanticModel)
		{
			// check if we need to resolve generic
			if (method.IsGenericMethod && method.TypeArguments.Any(x => x.TypeKind == TypeKind.TypeParameter))
			{
				// resolve generic parameters
				var parameters = method.TypeArguments;
				var typeParams = new ITypeSymbol[parameters.Length];
				for (int i = 0; i != typeParams.Length; ++i)
				{
					var resolvedType = ResolveType(parameters[i], usedWithinMethod, semanticModel);
					if (resolvedType == null) throw new Exception("Failed to resolve type for generic method: " + parameters[i].FullName());
					typeParams[i] = resolvedType;
				}

				// make sure we're constructing from generic source
				if (method.ConstructedFrom != null) method = method.ConstructedFrom.Construct(typeParams);
				else method = method.OriginalDefinition.Construct(typeParams);
				return ResolveMethod(method, usedWithinMethod, semanticModel);// keep processing until fully resolved
			}
			else if (method.ContainingType != null && method.ContainingType.IsGenericType && !IsResolvedGenericType(method.ContainingType))
			{
				// resolve generic type method lives in and find its matching method
				var resolvedType = ResolveType(method.ContainingType, usedWithinMethod, semanticModel);
				foreach (var member in resolvedType.GetMembers())
				{
					if (member.Kind != SymbolKind.Method) continue;
					var memberMethod = (IMethodSymbol)member;
					if (method.OriginalDefinition.Equals(memberMethod.OriginalDefinition))
					{
						return ResolveMethod(memberMethod, usedWithinMethod, semanticModel);// keep processing until fully resolved
					}
				}
			}

			return method;// full resolved
		}

		protected bool IsOfType(ITypeSymbol type, ITypeSymbol isType)
		{
			var next = type;
			while (next != null)
			{
				if (next.Equals(isType)) return true;
				next = next.BaseType;
			}
			return false;
		}

		protected bool HasInterface(ITypeSymbol type, ITypeSymbol interfaceType)
		{
			var next = type;
			while (next != null)
			{
				foreach (var i in next.Interfaces)
				{
					if (i.Equals(interfaceType)) return true;
					if (HasInterface(i, interfaceType)) return true;
				}
				next = next.BaseType;
			}
			return false;
		}

		protected bool IsIEnumerable_GetEnumerator(IMethodSymbol method)
		{
			if (method.MethodKind != MethodKind.ExplicitInterfaceImplementation) return false;
			if (!method.Name.EndsWith("GetEnumerator")) return false;
			if (method.ReturnType.TypeKind != TypeKind.Interface) return false;
			if (HasInterface(method.ContainingType, ienumerableT) && HasInterface(method.ContainingType, ienumerable)) return false;
			return true;
		}

		protected bool IsIEnumerator_Current(IMethodSymbol method)
		{
			if (method.MethodKind != MethodKind.PropertyGet) return false;
			if (!method.Name.EndsWith("get_Current")) return false;
			if (!objectType.Equals(method.ReturnType) || !HasInterface(method.ContainingType, ienumerator)) return false;
			return true;
		}

		protected bool IsResolvedGenericType(INamedTypeSymbol type)
		{
			if (!type.IsGenericType || type.IsDefinition) return false;
			var next = type;
			while (next != null)
			{
				if (next.TypeArguments.Any(x => x.TypeKind == TypeKind.TypeParameter)) return false;
				next = next.ContainingType;
			}
			return true;
		}

		protected bool IsResolvedGenericMethod(IMethodSymbol method)
		{
			if (!method.IsGenericMethod || method.IsDefinition) return false;
			if (method.TypeArguments.Any(x => x.TypeKind == TypeKind.TypeParameter)) return false;
			return !method.ContainingType.IsGenericType || IsResolvedGenericType(method.ContainingType);
		}

		protected bool NotReferenceableOnStack(IMethodSymbol method, ExpressionSyntax expression, SemanticModel semanticModel, out ExpressionSyntax outExpression, out ExpressionSyntax memberAccessExpression)
		{
			var currentSyntax = expression;
			while (currentSyntax != null)
			{
				if (currentSyntax is MemberAccessExpressionSyntax memberAccess)
				{
					var symbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
					var type = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
					if (symbol.Kind != SymbolKind.Local && type.IsReferenceType)
					{
						outExpression = memberAccess.Expression;
						memberAccessExpression = memberAccess.Name;
						return true;
					}
					currentSyntax = memberAccess.Expression;
					continue;
				}

				outExpression = null;
				memberAccessExpression = null;
				return false;
			}

			outExpression = null;
			memberAccessExpression = null;
			return false;
		}

		protected bool IgnoredSpecialGenericInterfaceMethod(IMethodSymbol method)
		{
			foreach (var interfaceMethod in method.ExplicitInterfaceImplementations)
			{
				if (interfaceMethod.Equals(ienumerableT_GetEnumerator) || interfaceMethod.Equals(ienumerable_GetEnumerator) || interfaceMethod.Equals(ienumerator_GetEnumerator))
				{
					return true;
				}
			}
			return false;
		}
	}
}
