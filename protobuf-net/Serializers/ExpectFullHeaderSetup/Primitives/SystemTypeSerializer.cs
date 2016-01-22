﻿// Modified by Vladyslav Taranov for AqlaSerializer, 2016
using System;

#if !NO_RUNTIME

#if FEAT_IKVM
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#else
using System.Reflection;
#endif

namespace AqlaSerializer.Serializers
{
    sealed class SystemTypeSerializer : IProtoSerializerWithAutoType
    {
#if FEAT_IKVM
        readonly Type expectedType;
#else
        static readonly Type expectedType = typeof(System.Type);
#endif
        public SystemTypeSerializer(AqlaSerializer.Meta.TypeModel model)
        {
#if FEAT_IKVM
            expectedType = model.MapType(typeof(System.Type));
#endif
        }
        public Type ExpectedType { get { return expectedType; } }

#if !FEAT_IKVM
        void IProtoSerializer.Write(object value, ProtoWriter dest)
        {
            ProtoWriter.WriteType((Type)value, dest);
        }

        object IProtoSerializer.Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value == null); // since replaces
            return source.ReadType();
        }
#endif
        bool IProtoSerializer.RequiresOldValue { get { return false; } }
        bool IProtoSerializer.ReturnsValue { get { return true; } }

#if FEAT_COMPILER
        void IProtoSerializer.EmitWrite(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicWrite("WriteType", valueFrom);
        }
        void IProtoSerializer.EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.EmitBasicRead("ReadType", ExpectedType);
        }
#endif
    }
}

#endif