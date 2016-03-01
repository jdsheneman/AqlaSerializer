﻿// Modified by Vladyslav Taranov for AqlaSerializer, 2016
#if !NO_RUNTIME
using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Threading;
using AltLinq;
using AqlaSerializer;
using AqlaSerializer.Internal;
using AqlaSerializer.Meta.Mapping;
using AqlaSerializer.Serializers;
using AqlaSerializer.Settings;
#if FEAT_IKVM
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#if FEAT_COMPILER
using IKVM.Reflection.Emit;
#endif
#else
using System.Reflection;
#if FEAT_COMPILER
using System.Reflection.Emit;
#endif
#endif


namespace AqlaSerializer.Meta
{
    partial class MetaType
    {
        // to be compatible with auxiliary type serializer and don't add overhead we don't decorate enums with netobject
        bool RootStartsGroup => RootNetObjectMode || RootLateReferenceMode;
        bool RootNetObjectMode => !IsSimpleValue && IsNetObjectValueDecoratorNecessary(_model, Type, true);
        bool RootLateReferenceMode => !IsSimpleValue && _model.ProtoCompatibility.UseOwnFormat;

        bool IsSimpleValue => Helpers.IsEnum(Type);

        void InitSerializers()
        {
            ThrowIfFrozen();
            IsFrozen = true;
            MemberLevelSettingsValue m = _settingsValue.Member;
            if (_settingsValue.IgnoreListHandling)
            {
                m.Collection.ItemType = null;
                m.Collection.Format = CollectionFormat.NotSpecified;
                m.Collection.PackedWireTypeForRead = null;
            }
            
            m.CollectionConcreteType = _settingsValue.ConstructType;

            if (_settingsValue.PrefixLength == null && !IsSimpleValue)
                _settingsValue.PrefixLength = true;
            
            _settingsValue.Member = m;
            
            _serializer = BuildSerializer(false);
            var s = BuildSerializer(true);
            _rootSerializer = new RootDecorator(
                Type,
                RootNetObjectMode,
                !RootLateReferenceMode,
                s,
                _model);
#if FEAT_COMPILER && !FX11
            if (_model.AutoCompile) CompileInPlace();
#endif
        }

        private IProtoTypeSerializer BuildSerializer(bool isRoot)
        {
            // reference tracking decorators (RootDecorator, NetObjectDecorator, NetObjectValueDecorator)
            // should always be applied only one time (otherwise will consider new objects as already written):
            // #1 For collection types references are handled either by RootDecorator or 
            // by ValueMember which owns the value (so outside of this scope)
            // because the value is treated as single object
            // #2 For members: ordinal ValueMembers are used and they will handle references when appropriate

            if (Helpers.IsEnum(Type))
            {
                Debug.Assert(IsSimpleValue);
                IProtoTypeSerializer ser = new WireTypeDecorator(WireType.Variant, new EnumSerializer(Type, GetEnumMap()));
                if (isRoot && !RootStartsGroup)
                    ser = new RootFieldNumberDecorator(ser, ListHelpers.FieldItem);
                return ser;
            }

            Type itemType = IgnoreListHandling
                                ? null
                                : (_settingsValue.Member.Collection.ItemType ?? (Type.IsArray ? Type.GetElementType() : TypeModel.GetListItemType(_model, Type)));

            if (itemType != null)
            {

                if (_surrogate != null)
                {
                    throw new ArgumentException("Repeated data (a list, collection, etc) has inbuilt behaviour and cannot use a surrogate");
                }

                Type defaultType = null;
                ResolveListTypes(_model, Type, ref itemType, ref defaultType);
                
                if (_fields.Count != 0)
                    throw new ArgumentException("Repeated data (an array, list, etc) has inbuilt behavior and can't have fields");

                // apply default member settings to type settings too
                var s = _settingsValue.Member;
                // but change this:
                s.EffectiveType = Type; // not merged with anything so assign
                s.CollectionConcreteType = _settingsValue.ConstructType ?? defaultType;
                s.Collection.Append = false; // allowed only on members
                s.WriteAsDynamicType = false; // allowed only on members
                // this should be handled as collection
                if (s.Collection.ItemType == null) s.Collection.ItemType = itemType;

                WireType wt;
                var ser = (IProtoTypeSerializer)
                       _model.ValueSerializerBuilder.BuildValueFinalSerializer(
                           new ValueSerializationSettings(new MemberLevelSettingsValue?[] { s }, s.MakeDefaultNestedLevel()),
                           false, out wt);

                // standard root decorator won't start any field
                // in compatibility mode collections won't start subitems like normally
                // so wrap with field
                if (isRoot && !RootStartsGroup)
                    ser = new RootFieldNumberDecorator(ser, TypeModel.EnumRootTag);

                return ser;
            }

            if (BaseType != null && !BaseType.IgnoreListHandling && RuntimeTypeModel.CheckTypeIsCollection(_model, BaseType.Type))
                throw new ArgumentException("A subclass of a repeated data (an array, list, etc should be handled too as a collection");

            // #2

            if (_surrogate != null)
            {
                MetaType mt = _model[_surrogate], mtBase;
                while ((mtBase = mt.BaseType) != null) { mt = mtBase; }
                return new SurrogateSerializer(_model, Type, _surrogate, mt.Serializer);
            }
            if (IsAutoTuple)
            {
                MemberInfo[] mapping;
                ConstructorInfo ctor = ResolveTupleConstructor(Type, out mapping);
                if (ctor == null) throw new InvalidOperationException();
                return new TupleSerializer(_model, ctor, mapping, _settingsValue.PrefixLength.GetValueOrDefault(true));
            }


            _fields.Trim();
            int fieldCount = _fields.Count;
            int subTypeCount = _subTypes == null ? 0 : _subTypes.Count;
            int[] fieldNumbers = new int[fieldCount + subTypeCount];
            IProtoSerializerWithWireType[] serializers = new IProtoSerializerWithWireType[fieldCount + subTypeCount];
            int i = 0;
            if (subTypeCount != 0)
            {
                foreach (SubType subType in _subTypes)
                {
#if WINRT
                    if (!subType.DerivedType.IgnoreListHandling && ienumerable.IsAssignableFrom(subType.DerivedType.Type.GetTypeInfo()))
#else
                    if (!subType.DerivedType.IgnoreListHandling && _model.MapType(ienumerable).IsAssignableFrom(subType.DerivedType.Type))
#endif
                    {
                        throw new ArgumentException("Repeated data (a list, collection, etc) has inbuilt behaviour and cannot be used as a subclass");
                    }
                    fieldNumbers[i] = subType.FieldNumber;
                    serializers[i++] = subType.Serializer;
                }
            }
            if (fieldCount != 0)
            {
                foreach (ValueMember member in _fields)
                {
                    fieldNumbers[i] = member.FieldNumber;
                    serializers[i++] = member.Serializer;
                }
            }

            BasicList baseCtorCallbacks = null;
            MetaType tmp = BaseType;

            while (tmp != null)
            {
                MethodInfo method = tmp.HasCallbacks ? tmp.Callbacks.BeforeDeserialize : null;
                if (method != null)
                {
                    if (baseCtorCallbacks == null) baseCtorCallbacks = new BasicList();
                    baseCtorCallbacks.Add(method);
                }
                tmp = tmp.BaseType;
            }
            MethodInfo[] arr = null;
            if (baseCtorCallbacks != null)
            {
                arr = new MethodInfo[baseCtorCallbacks.Count];
                baseCtorCallbacks.CopyTo(arr, 0);
                Array.Reverse(arr);
            }
            return new TypeSerializer(_model, Type, fieldNumbers, serializers, arr, BaseType == null, !_settingsValue.SkipConstructor, _callbacks, _settingsValue.ConstructType, _factory, _settingsValue.PrefixLength.Value);
        }



#if FEAT_IKVM || !FEAT_COMPILER
        internal bool IsCompiledInPlace => false;
#else
        internal bool IsCompiledInPlace => _serializer is CompiledSerializer;
#endif

        public bool IsSerializerReady
        {
            get
            {
#if !WINRT
                Thread.MemoryBarrier();
#else
                Interlocked.MemoryBarrier();
#endif
                return _serializer != null;
            }
        }

        [DebuggerHidden]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        IProtoSerializerWithWireType ISerializerProxy.Serializer => Serializer;

#if FEAT_COMPILER && !FX11

        /// <summary>
        /// Compiles the serializer for this type; this is *not* a full
        /// standalone compile, but can significantly boost performance
        /// while allowing additional types to be added.
        /// </summary>
        /// <remarks>An in-place compile can access non-public types / members</remarks>
        public void CompileInPlace()
        {
            var s = Serializer;
            var r = RootSerializer;
#if FAKE_COMPILE
            return;
#endif
#if FEAT_IKVM
            // just no nothing, quietely; don't want to break the API
#else
            if (s is CompiledSerializer) return;
            _serializer = CompiledSerializer.Wrap(s, _model);
            _rootSerializer = CompiledSerializer.Wrap(r, _model);
#endif
        }
#endif


        internal bool IsPrepared()
        {
#if FEAT_COMPILER && !FEAT_IKVM && !FX11
            return _serializer is CompiledSerializer;
#else
            return false;
#endif
        }


        private IProtoTypeSerializer _serializer;
        [DebuggerHidden]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal IProtoTypeSerializer Serializer
        {
            get
            {
                if (_serializer == null)
                {
                    int opaqueToken = 0;
                    try
                    {
                        _model.TakeLock(ref opaqueToken);
                        if (_serializer == null)
                        { // double-check, but our main purpse with this lock is to ensure thread-safety with
                            // serializers needing to wait until another thread has finished adding the properties
                            InitSerializers();
                        }
                    }
                    finally
                    {
                        _model.ReleaseLock(opaqueToken);
                    }
                }
                return _serializer;
            }
        }

        private IProtoTypeSerializer _rootSerializer;

        [DebuggerHidden]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal IProtoTypeSerializer RootSerializer
        {
            get
            {
                if (_rootSerializer == null)
                {
                    int opaqueToken = 0;
                    try
                    {
                        _model.TakeLock(ref opaqueToken);
                        if (_rootSerializer == null)
                        { // double-check, but our main purpse with this lock is to ensure thread-safety with
                            // serializers needing to wait until another thread has finished adding the properties
                            InitSerializers();
                        }
                    }
                    finally
                    {
                        _model.ReleaseLock(opaqueToken);
                    }
                }
                return _rootSerializer;
            }
        }
    }
}
#endif