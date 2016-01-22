﻿// Modified by Vladyslav Taranov for AqlaSerializer, 2016
#if !NO_RUNTIME
using System;
using System.Collections;
using System.Collections.Generic;
#if FEAT_COMPILER
using AqlaSerializer.Compiler;
#endif
using AqlaSerializer.Meta;

#if FEAT_IKVM
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#else
using System.Reflection;
#endif

namespace AqlaSerializer.Serializers
{
    sealed class ImmutableCollectionDecorator : ListDecorator
    {
        protected override bool RequireAdd { get { return false; } }
#if !NO_GENERICS

        static Type ResolveIReadOnlyCollection(Type declaredType, Type t)
        {
#if WINRT
            if (CheckIsIReadOnlyCollectionExactly(declaredType.GetTypeInfo())) return declaredType;
            foreach (Type intImplBasic in declaredType.GetTypeInfo().ImplementedInterfaces)
            {
                TypeInfo intImpl = intImplBasic.GetTypeInfo();
                if (CheckIsIReadOnlyCollectionExactly(intImpl)) return intImplBasic;
            }
#else
            if (CheckIsIReadOnlyCollectionExactly(declaredType)) return declaredType;
            foreach (Type intImpl in declaredType.GetInterfaces())
            {
                if (CheckIsIReadOnlyCollectionExactly(intImpl)) return intImpl;
            }
#endif
            return null;
        }

#if WINRT
        static bool CheckIsIReadOnlyCollectionExactly(TypeInfo t)
#else
        static bool CheckIsIReadOnlyCollectionExactly(Type t)
#endif
        {
            if (t != null && t.IsGenericType && t.Name.StartsWith("IReadOnlyCollection`"))
            {
#if WINRT
                Type[] typeArgs = t.GenericTypeArguments;
                if (typeArgs.Length != 1 && typeArgs[0].GetTypeInfo().Equals(t)) return false;
#else
                Type[] typeArgs = t.GetGenericArguments();
                if (typeArgs.Length != 1 && typeArgs[0] != t) return false;
#endif

                return true;
            }
            return false;
        }

        internal static bool IdentifyImmutable(TypeModel model, Type declaredType, out MethodInfo builderFactory, out MethodInfo add, out MethodInfo addRange, out MethodInfo finish)
        {
            builderFactory = add = addRange = finish = null;
            if (model == null || declaredType == null) return false;
#if WINRT
            TypeInfo declaredTypeInfo = declaredType.GetTypeInfo();
#else
            Type declaredTypeInfo = declaredType;
#endif

            // try to detect immutable collections; firstly, they are all generic, and all implement IReadOnlyCollection<T> for some T
            if(!declaredTypeInfo.IsGenericType) return false;

#if WINRT
            Type[] typeArgs = declaredTypeInfo.GenericTypeArguments, effectiveType;
#else
            Type[] typeArgs = declaredTypeInfo.GetGenericArguments(), effectiveType;
#endif
            switch (typeArgs.Length)
            {
                case 1:
                    effectiveType = typeArgs;
                    break; // fine
                case 2:
                    Type kvp = model.MapType(typeof(System.Collections.Generic.KeyValuePair<,>));
                    if (kvp == null) return false;
                    kvp = kvp.MakeGenericType(typeArgs);
                    effectiveType = new Type[] { kvp };
                    break;
                default:
                    return false; // no clue!
            }

            if (ResolveIReadOnlyCollection(declaredType, null) == null) return false; // no IReadOnlyCollection<T> found
            
            // and we want to use the builder API, so for generic Foo<T> or IFoo<T> we want to use Foo.CreateBuilder<T>
            string name = declaredType.Name;
            int i = name.IndexOf('`');
            if (i <= 0) return false;
            name = declaredTypeInfo.IsInterface ? name.Substring(1, i - 1) : name.Substring(0, i);

            Type outerType = model.GetType(declaredType.Namespace + "." + name, declaredTypeInfo.Assembly);
            // I hate special-cases...
            if (outerType == null && name == "ImmutableSet")
            {
                outerType = model.GetType(declaredType.Namespace + ".ImmutableHashSet", declaredTypeInfo.Assembly);
            }
            if (outerType == null) return false;

#if WINRT
            foreach (MethodInfo method in outerType.GetTypeInfo().DeclaredMethods)
#else
            foreach (MethodInfo method in outerType.GetMethods())
#endif
            {
                if (!method.IsStatic || method.Name != "CreateBuilder" || !method.IsGenericMethodDefinition || method.GetParameters().Length != 0
                    || method.GetGenericArguments().Length != typeArgs.Length) continue;

                builderFactory = method.MakeGenericMethod(typeArgs);
                break;
            }
            Type voidType = model.MapType(typeof(void));
            if (builderFactory == null || builderFactory.ReturnType == null || builderFactory.ReturnType == voidType) return false;


            add = Helpers.GetInstanceMethod(builderFactory.ReturnType, "Add", effectiveType);
            if (add == null) return false;

            finish = Helpers.GetInstanceMethod(builderFactory.ReturnType, "ToImmutable", Helpers.EmptyTypes);
            if (finish == null || finish.ReturnType == null || finish.ReturnType == voidType) return false;

            if (!(finish.ReturnType == declaredType || Helpers.IsAssignableFrom(declaredType, finish.ReturnType))) return false;

            addRange = Helpers.GetInstanceMethod(builderFactory.ReturnType, "AddRange", new Type[] { declaredType });
            if (addRange == null)
            {
                Type enumerable = model.MapType(typeof(System.Collections.Generic.IEnumerable<>), false);
                if (enumerable != null)
                {
                    addRange = Helpers.GetInstanceMethod(builderFactory.ReturnType, "AddRange", new Type[] { enumerable.MakeGenericType(effectiveType) });
                }
            }

            return true;
        }
#endif

        private readonly MethodInfo builderFactory, add, addRange, finish;
        internal ImmutableCollectionDecorator(TypeModel model, Type declaredType, Type concreteType, IProtoSerializerWithWireType tail, bool writePacked, WireType packedWireType, bool returnList, bool overwriteList,
            MethodInfo builderFactory, MethodInfo add, MethodInfo addRange, MethodInfo finish, KeyValuePair<Type, int>[] subtypeNumbers, bool protoCompatibility)
            : base(model, declaredType, concreteType, tail, writePacked, packedWireType, returnList, overwriteList, subtypeNumbers, protoCompatibility)
        {
            this.builderFactory = builderFactory;
            this.add = add;
            this.addRange = addRange;
            this.finish = finish;
            _listHelpers = new ListHelpers(WritePacked, _packedWireTypeForRead, _protoCompatibility, Tail);
        }

        readonly ListHelpers _listHelpers;

#if !FEAT_IKVM


        public override object CreateInstance(ProtoReader source)
        {
            object builderInstance = builderFactory.Invoke(null, null);
            return finish.Invoke(builderInstance, null);
        }

        public override object Read(object value, ProtoReader source)
        {
            object builderInstance = builderFactory.Invoke(null, null);
            object[] args = new object[1];

            if (AppendToCollection && value != null && ((IList)value).Count != 0)
            {   
                if(addRange !=null)
                {
                    args[0] = value;
                    addRange.Invoke(builderInstance, args);
                }
                else
                {
                    foreach(object item in (IList)value)
                    {
                        args[0] = item;
                        add.Invoke(builderInstance, args);
                    }
                }
            }

            _listHelpers.Read(null,
                o =>
                    {
                        args[0] = o;
                        add.Invoke(builderInstance, args);
                    },
                source);
            
            return finish.Invoke(builderInstance, null);
        }

        protected override bool WriteSubtypeInfo => false;
#endif

#if FEAT_COMPILER

        public override void EmitCreateInstance(CompilerContext ctx)
        {
            using (Compiler.Local builder = new Compiler.Local(ctx, builderFactory.ReturnType))
            {
                ctx.EmitCall(builderFactory);
                ctx.LoadAddress(builder, builder.Type);
                ctx.EmitCall(finish);
            }
        }

        protected override void EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
#if false
            using (Compiler.Local oldList = AppendToCollection ? ctx.GetLocalWithValue(ExpectedType, valueFrom) : null)
            using(Compiler.Local builder = new Compiler.Local(ctx, builderFactory.ReturnType))
            {
                ctx.EmitCall(builderFactory);
                ctx.StoreValue(builder);

                if(AppendToCollection)
                {
                    Compiler.CodeLabel done = ctx.DefineLabel();
                    if(!ExpectedType.IsValueType)
                    {
                        ctx.LoadValue(oldList);
                        ctx.BranchIfFalse(done, false); // old value null; nothing to add
                    }
                    PropertyInfo prop = Helpers.GetProperty(ExpectedType, "Length", false);
                    if(prop == null) prop = Helpers.GetProperty(ExpectedType, "Count", false);
#if !NO_GENERICS
                    if (prop == null) prop = Helpers.GetProperty(ResolveIReadOnlyCollection(ExpectedType, Tail.ExpectedType), "Count", false);
#endif
                    ctx.LoadAddress(oldList, oldList.Type);
                    ctx.EmitCall(Helpers.GetGetMethod(prop, false, false));
                    ctx.BranchIfFalse(done, false); // old list is empty; nothing to add

                    Type voidType = ctx.MapType(typeof(void));
                    if(addRange != null)
                    {
                        ctx.LoadValue(builder);
                        ctx.LoadValue(oldList);
                        ctx.EmitCall(addRange);
                        if (addRange.ReturnType != null && add.ReturnType != voidType) ctx.DiscardValue();
                    }
                    else
                    {
                        // loop and call Add repeatedly
                        MethodInfo moveNext, current, getEnumerator = GetEnumeratorInfo(ctx.Model, out moveNext, out current);
                        Helpers.DebugAssert(moveNext != null);
                        Helpers.DebugAssert(current != null);
                        Helpers.DebugAssert(getEnumerator != null);

                        Type enumeratorType = getEnumerator.ReturnType;
                        using (Compiler.Local iter = new Compiler.Local(ctx, enumeratorType))
                        {
                            ctx.LoadAddress(oldList, ExpectedType);
                            ctx.EmitCall(getEnumerator);
                            ctx.StoreValue(iter);
                            using (ctx.Using(iter))
                            {
                                Compiler.CodeLabel body = ctx.DefineLabel(), next = ctx.DefineLabel();
                                ctx.Branch(next, false);

                                ctx.MarkLabel(body);
                                ctx.LoadAddress(builder, builder.Type);
                                ctx.LoadAddress(iter, enumeratorType);                                
                                ctx.EmitCall(current);
                                ctx.EmitCall(add);
                                if (add.ReturnType != null && add.ReturnType != voidType) ctx.DiscardValue();

                                ctx.MarkLabel(@next);
                                ctx.LoadAddress(iter, enumeratorType);
                                ctx.EmitCall(moveNext);
                                ctx.BranchIfTrue(body, false);
                            }
                        }
                    }


                    ctx.MarkLabel(done);
                }

                EmitReadList(ctx, builder, Tail, add, packedWireType, false);

                ctx.LoadAddress(builder, builder.Type);
                ctx.EmitCall(finish);
                if(ExpectedType != finish.ReturnType)
                {
                    ctx.Cast(ExpectedType);
                }
            }
#endif

        }
#endif
    }
}
#endif