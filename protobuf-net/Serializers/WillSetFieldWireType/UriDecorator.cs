﻿// Modified by Vladyslav Taranov for AqlaSerializer, 2016
#if !NO_RUNTIME
using System;

#if FEAT_IKVM
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#else
using System.Reflection;
#endif

namespace AqlaSerializer.Serializers
{
    sealed class UriDecorator : ProtoDecoratorBase, IProtoSerializerWithWireType
    {
#if FEAT_IKVM
        readonly Type expectedType;
#else
        static readonly Type expectedType = typeof(Uri);
#endif
        public UriDecorator(AqlaSerializer.Meta.TypeModel model, IProtoSerializerWithWireType tail)
            : base(tail)
        {
#if FEAT_IKVM
            expectedType = model.MapType(typeof(Uri));
#endif
        }
        public override Type ExpectedType { get { return expectedType; } }
        public override bool RequiresOldValue { get { return false; } }
        public override bool ReturnsValue { get { return true; } }


#if !FEAT_IKVM
        public override void Write(object value, ProtoWriter dest)
        {
            Tail.Write(((Uri)value).AbsoluteUri, dest);
        }
        public override object Read(object value, ProtoReader source)
        {
            Helpers.DebugAssert(value == null); // not expecting incoming
            string s = (string)Tail.Read(null, source);
            return s.Length == 0 ? null : CreateUri(s, source);
        }

        private Uri CreateUri(string s, ProtoReader source)
        {
            var u = new Uri(s);
            ProtoReader.NoteObject(u, source);
            return u;
        }
#endif

#if FEAT_COMPILER
        protected override void EmitWrite(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            ctx.LoadValue(valueFrom);
            ctx.LoadValue(typeof(Uri).GetProperty("AbsoluteUri"));
            Tail.EmitWrite(ctx, null);
        }
        protected override void EmitRead(Compiler.CompilerContext ctx, Compiler.Local valueFrom)
        {
            Tail.EmitRead(ctx, valueFrom);
            ctx.CopyValue();
            Compiler.CodeLabel @nonEmpty = ctx.DefineLabel(), @end = ctx.DefineLabel();
            ctx.LoadValue(typeof(string).GetProperty("Length"));
            ctx.BranchIfTrue(@nonEmpty, true);
            ctx.DiscardValue();
            ctx.LoadNullRef();
            ctx.Branch(@end, true);
            ctx.MarkLabel(@nonEmpty);
            ctx.EmitCtor(ctx.MapType(typeof(Uri)), ctx.MapType(typeof(string)));
            ctx.CopyValue();
            ctx.CastToObject(ctx.MapType(typeof(Uri)));
            ctx.EmitCallNoteObject();
            ctx.MarkLabel(@end);

        }
#endif
    }
}
#endif