﻿// Modified by Vladyslav Taranov for AqlaSerializer, 2016
#if !NO_RUNTIME
using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using AqlaSerializer;
using AqlaSerializer.Serializers;


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
    /// <summary>
    /// Represents a type at runtime for use with protobuf, allowing the field mappings (etc) to be defined
    /// </summary>
    public class MetaType : ISerializerProxy
    {
        internal sealed class Comparer : IComparer
#if !NO_GENERICS
             , System.Collections.Generic.IComparer<MetaType>
#endif
        {
            public static readonly Comparer Default = new Comparer();
            public int Compare(object x, object y)
            {
                return Compare(x as MetaType, y as MetaType);
            }
            public int Compare(MetaType x, MetaType y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

#if FX11
                return string.Compare(x.GetSchemaTypeName(), y.GetSchemaTypeName());
#else
                return string.Compare(x.GetSchemaTypeName(), y.GetSchemaTypeName(), StringComparison.Ordinal);
#endif
            }
        }
        /// <summary>
        /// Get the name of the type being represented
        /// </summary>
        public override string ToString()
        {
            return type.ToString();
        }
        [DebuggerHidden]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        IProtoSerializerWithWireType ISerializerProxy.Serializer { get { return Serializer; } }
        private MetaType baseType;
        /// <summary>
        /// Gets the base-type for this type
        /// </summary>
        public MetaType BaseType {
            get { return baseType; }
        }
        internal TypeModel Model { get { return model; } }

        /// <summary>
        /// When used to compile a model, should public serialization/deserialzation methods
        /// be included for this type?
        /// </summary>
        public bool IncludeSerializerMethod
        {   // negated to minimize common-case / initializer
            get { return !HasFlag(OPTIONS_PrivateOnApi); }
            set { SetFlag(OPTIONS_PrivateOnApi, !value, true); }
        }

        /// <summary>
        /// Should this type be treated as a reference by default FOR MISSING TYPE MEMBERS ONLY?
        /// </summary>
        public bool AsReferenceDefault
        { 
            get { return HasFlag(OPTIONS_AsReferenceDefault); }
            set { SetFlag(OPTIONS_AsReferenceDefault, value, true); }
        }

        Type _collectionConcreteType;

        public Type CollectionConcreteType
        {
            get
            {
                return _collectionConcreteType;
            }
            set
            {
                if (value != null && !Helpers.IsAssignableFrom(this.Type, value))
                    throw new ArgumentException("Specified collection concrete type " + value.Name + " is not assignable to " + this.Type.Name);
                _collectionConcreteType = value;
            }
        }

        public bool PrefixLength { get; set; } = true;

        public BinaryDataFormat CollectionDataFormat { get; set; }
        
        private BasicList subTypes;
        private BasicList subTypesSimple;

        public bool IsValidSubType(Type subType)
        {
#if WINRT
            if (!CanHaveSubType(typeInfo)) return false;
#else
            if (!CanHaveSubType(type)) return false;
#endif
#if WINRT
            return typeInfo.IsAssignableFrom(subType.GetTypeInfo());
#else
            return type.IsAssignableFrom(subType);
#endif
        }
#if WINRT       
        public static bool CanHaveSubType(Type type)
        {
            return CanHaveSubType(type.GetTypeInfo());
        }

        public static bool CanHaveSubType(TypeInfo type)
#else
        public static bool CanHaveSubType(Type type)
#endif
        {
#if WINRT
            if ((type.IsClass || type.IsInterface) && !type.IsSealed)
            {
#else
            if ((type.IsClass || type.IsInterface) && !type.IsSealed)
            {
                return true;
#endif

            }
            return false;
        }

        bool _isDefaultBehaviourApplied;

        public void ApplyDefaultBehaviour()
        {
            // AddSubType is not thread safe too, so what?
            if (_isDefaultBehaviourApplied) return;
            _isDefaultBehaviourApplied = true;
            model.AutoAddStrategy.ApplyDefaultBehaviour(this);
        }

        // TODO throw exception duplicate field number when adding (now when building serializers)

        /// <summary>
        /// Adds a known sub-type to the inheritance model
        /// </summary>
        public MetaType AddSubType(int fieldNumber, Type derivedType)
        {
            return AddSubType(fieldNumber, derivedType, BinaryDataFormat.Default);
        }
        /// <summary>
        /// Adds a known sub-type to the inheritance model
        /// </summary>
        public MetaType AddSubType(int fieldNumber, Type derivedType, BinaryDataFormat dataFormat)
        {
            if (derivedType == null) throw new ArgumentNullException("derivedType");
            if (fieldNumber < 1) throw new ArgumentOutOfRangeException("fieldNumber");
#if WINRT
            if (!CanHaveSubType(typeInfo)) {
#else
            if (!CanHaveSubType(type)) {
#endif
                throw new InvalidOperationException("Sub-types can only be added to non-sealed classes");
            }
            if (!IsValidSubType(derivedType))
            {
                throw new ArgumentException(derivedType.Name + " is not a valid sub-type of " + type.Name, "derivedType");
            }

            if (subTypesSimple !=null && subTypesSimple.Contains(derivedType)) return this; // already exists
            
            if (subTypesSimple == null) subTypesSimple = new BasicList();
            subTypesSimple.Add(derivedType);

            MetaType derivedMeta = model[derivedType];
            ThrowIfFrozen();
            derivedMeta.ThrowIfFrozen();
            SubType subType = new SubType(fieldNumber, derivedMeta, dataFormat);
            ThrowIfFrozen();

            derivedMeta.SetBaseType(this); // includes ThrowIfFrozen
            if (subTypes == null) subTypes = new BasicList();
            subTypes.Add(subType);
            
            return this;
        }

        public int GetNextFreeFieldNumber()
        {
            return GetNextFreeFieldNumber(1);
        }

        public int GetNextFreeFieldNumber(int start)
        {
            int number = start - 1;
            bool found;
            do
            {
                if (number++ == short.MaxValue) return -1;
                found = false;
                // they are not sorted, so...
                foreach (ValueMember f in Fields)
                {
                    if (f.FieldNumber == number)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (subTypes != null)
                        foreach (SubType t in subTypes)
                        {
                            if (t.FieldNumber == number)
                            {
                                found = true;
                                break;
                            }
                        }
                }
            } while (found);
            return number;
        }

#if WINRT
        internal static readonly TypeInfo ienumerable = typeof(IEnumerable).GetTypeInfo();
#else
        internal static readonly System.Type ienumerable = typeof(IEnumerable);
#endif
        private void SetBaseType(MetaType baseType)
        {
            if (baseType == null) throw new ArgumentNullException("baseType");
            if (this.baseType == baseType) return;
            if (this.baseType != null) throw new InvalidOperationException("A type can only participate in one inheritance hierarchy");

            MetaType type = baseType;
            while (type != null)
            {
                if (ReferenceEquals(type, this)) throw new InvalidOperationException("Cyclic inheritance is not allowed");
                type = type.baseType;
            }
            this.baseType = baseType;
        }

        private CallbackSet callbacks;
        /// <summary>
        /// Indicates whether the current type has defined callbacks 
        /// </summary>
        public bool HasCallbacks
        {
            get { return callbacks != null && callbacks.NonTrivial; }
        }

        /// <summary>
        /// Indicates whether the current type has defined subtypes
        /// </summary>
        public bool HasSubtypes
        {
            get { return subTypes != null && subTypes.Count != 0; }
        }

        /// <summary>
        /// Returns the set of callbacks defined for this type
        /// </summary>
        public CallbackSet Callbacks
        {
            get
            {
                if (callbacks == null) callbacks = new CallbackSet(this);
                return callbacks;
            }
        }

        private bool IsValueType
        {
            get
            {
#if WINRT
                return typeInfo.IsValueType;
#else
                return type.IsValueType;
#endif
            }
        }
        /// <summary>
        /// Assigns the callbacks to use during serialiation/deserialization.
        /// </summary>
        /// <param name="beforeSerialize">The method (or null) called before serialization begins.</param>
        /// <param name="afterSerialize">The method (or null) called when serialization is complete.</param>
        /// <param name="beforeDeserialize">The method (or null) called before deserialization begins (or when a new instance is created during deserialization).</param>
        /// <param name="afterDeserialize">The method (or null) called when deserialization is complete.</param>
        /// <returns>The set of callbacks.</returns>
        public MetaType SetCallbacks(MethodInfo beforeSerialize, MethodInfo afterSerialize, MethodInfo beforeDeserialize, MethodInfo afterDeserialize)
        {
            CallbackSet callbacks = Callbacks;
            callbacks.BeforeSerialize = beforeSerialize;
            callbacks.AfterSerialize = afterSerialize;
            callbacks.BeforeDeserialize = beforeDeserialize;
            callbacks.AfterDeserialize = afterDeserialize;
            return this;
        }
        /// <summary>
        /// Assigns the callbacks to use during serialiation/deserialization.
        /// </summary>
        /// <param name="beforeSerialize">The name of the method (or null) called before serialization begins.</param>
        /// <param name="afterSerialize">The name of the method (or null) called when serialization is complete.</param>
        /// <param name="beforeDeserialize">The name of the method (or null) called before deserialization begins (or when a new instance is created during deserialization).</param>
        /// <param name="afterDeserialize">The name of the method (or null) called when deserialization is complete.</param>
        /// <returns>The set of callbacks.</returns>
        public MetaType SetCallbacks(string beforeSerialize, string afterSerialize, string beforeDeserialize, string afterDeserialize)
        {
            if (IsValueType) throw new InvalidOperationException();
            CallbackSet callbacks = Callbacks;
            callbacks.BeforeSerialize = ResolveMethod(beforeSerialize, true);
            callbacks.AfterSerialize = ResolveMethod(afterSerialize, true);
            callbacks.BeforeDeserialize = ResolveMethod(beforeDeserialize, true);
            callbacks.AfterDeserialize = ResolveMethod(afterDeserialize, true);
            return this;
        }

        internal string GetSchemaTypeName()
        {
            if (surrogate != null) return model[surrogate].GetSchemaTypeName();

            if (!Helpers.IsNullOrEmpty(name)) return name;

            string typeName = type.Name;
#if !NO_GENERICS
            if (type
#if WINRT
                .GetTypeInfo()
#endif       
                .IsGenericType)
            {
                StringBuilder sb = new StringBuilder(typeName);
                int split = typeName.IndexOf('`');
                if (split >= 0) sb.Length = split;
                foreach (Type arg in type
#if WINRT
                    .GetTypeInfo().GenericTypeArguments
#else               
                    .GetGenericArguments()
#endif
                    )
                {
                    sb.Append('_');
                    Type tmp = arg;
                    int key = model.GetKey(ref tmp);
                    MetaType mt;
                    if (key >= 0 && (mt = model[tmp]) != null && mt.surrogate == null) // <=== need to exclude surrogate to avoid chance of infinite loop
                    {
                        
                        sb.Append(mt.GetSchemaTypeName());
                    }
                    else
                    {
                        sb.Append(tmp.Name);
                    }
                }
                return sb.ToString();
            }
#endif
            return typeName;
        }

        private string name;
        /// <summary>
        /// Gets or sets the name of this contract.
        /// </summary>
        public string Name
        {
            get
            {
                return name;
            }
            set
            {
                ThrowIfFrozen();
                name = value;
            }
        }

        private MethodInfo factory;
        /// <summary>
        /// Designate a factory-method to use to create instances of this type
        /// </summary>
        public MetaType SetFactory(MethodInfo factory)
        {
            model.VerifyFactory(factory, type);
            ThrowIfFrozen();
            this.factory = factory;
            return this;
        }



        /// <summary>
        /// Designate a factory-method to use to create instances of this type
        /// </summary>
        public MetaType SetFactory(string factory)
        {
            return SetFactory(ResolveMethod(factory, false));
        }

        private MethodInfo ResolveMethod(string name, bool instance)
        {
            if (Helpers.IsNullOrEmpty(name)) return null;
#if WINRT
            return instance ? Helpers.GetInstanceMethod(typeInfo, name) : Helpers.GetStaticMethod(typeInfo, name);
#else
            return instance ? Helpers.GetInstanceMethod(type, name) : Helpers.GetStaticMethod(type, name);
#endif
        }
        private readonly RuntimeTypeModel model;
        internal static Exception InbuiltType(Type type)
        {
            return new ArgumentException("Data of this type has inbuilt behaviour, and cannot be added to a model in this way: " + type.FullName);
        }
        internal MetaType(RuntimeTypeModel model, Type type, MethodInfo factory)
        {
            this.factory = factory;
            if (model == null) throw new ArgumentNullException("model");
            if (type == null) throw new ArgumentNullException("type");
            
            if (model.AutoAddStrategy.GetAsReferenceDefault(type, false))
                AsReferenceDefault = true;

            IProtoSerializer coreSerializer = model.TryGetBasicTypeSerializer(type);
            if (coreSerializer != null)
            {
                throw InbuiltType(type);
            }
            
            this.type = type;
#if WINRT
            this.typeInfo = type.GetTypeInfo();
#endif
            this.model = model;
            
            if (Helpers.IsEnum(type))
            {
#if WINRT
                EnumPassthru = typeInfo.IsDefined(typeof(FlagsAttribute), false);
#else
                EnumPassthru = type.IsDefined(model.MapType(typeof(FlagsAttribute)), false);
#endif
            }
        }
#if WINRT
        private readonly TypeInfo typeInfo;
#endif
        /// <summary>
        /// Throws an exception if the type has been made immutable
        /// </summary>
        protected internal void ThrowIfFrozen()
        {
            if ((flags & OPTIONS_Frozen)!=0) throw new InvalidOperationException("The type cannot be changed once a serializer has been generated for " + type.FullName);
        }
        //internal void Freeze() { flags |= OPTIONS_Frozen; }

        private readonly Type type;
        /// <summary>
        /// The runtime type that the meta-type represents
        /// </summary>
        public Type Type { get { return type; } }
        private IProtoTypeSerializer serializer;
        [DebuggerHidden]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal IProtoTypeSerializer Serializer {
            get {
                if (serializer == null)
                {
                    int opaqueToken = 0;
                    try
                    {
                        model.TakeLock(ref opaqueToken);
                        if (serializer == null)
                        { // double-check, but our main purpse with this lock is to ensure thread-safety with
                            // serializers needing to wait until another thread has finished adding the properties
                            InitSerializers();
                        }
                    }
                    finally
                    {
                        model.ReleaseLock(opaqueToken);
                    }
                }
                return serializer;
            }
        }

        void InitSerializers()
        {
            SetFlag(OPTIONS_Frozen, true, false);
            serializer = BuildSerializer(false);
            var s = BuildSerializer(true);
            rootSerializer = new RootDecorator(type, !Helpers.IsValueType(type) && model.ProtoCompatibility.AllowExtensionDefinitions.HasFlag(NetObjectExtensionTypes.Reference), s);
#if FEAT_COMPILER && !FX11
            if (model.AutoCompile) CompileInPlace();
#endif
        }

        private IProtoTypeSerializer rootSerializer;

        [DebuggerHidden]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal IProtoTypeSerializer RootSerializer
        {
            get
            {
                if (rootSerializer == null)
                {
                    int opaqueToken = 0;
                    try
                    {
                        model.TakeLock(ref opaqueToken);
                        if (rootSerializer == null)
                        { // double-check, but our main purpse with this lock is to ensure thread-safety with
                            // serializers needing to wait until another thread has finished adding the properties
                            InitSerializers();
                        }
                    }
                    finally
                    {
                        model.ReleaseLock(opaqueToken);
                    }
                }
                return rootSerializer;
            }
        }


        internal bool IsList
        {
            get
            {
                Type itemType = IgnoreListHandling ? null : TypeModel.GetListItemType(model, type);
                return itemType != null;
            }
        }
        private IProtoTypeSerializer BuildSerializer(bool isRoot)
        {
            // reference tracking decorators (RootDecorator, NetObjectDecorator, NetObjectValueDecorator)
            // should always be applied only one time (otherwise will consider new objects as already written):
            // #1 For collection types references are handled either by RootDecorator or 
            // by VirtualMember which owns the value (so outside of this scope)
            // because the value is treated as single object
            // #2 For members: ordinal VirtualMembers are used and they will handle references when appropriate

            if (Helpers.IsEnum(type))
                return new WireTypeDecorator(WireType.Variant, new EnumSerializer(type, GetEnumMap()));
            
            Type itemType = IgnoreListHandling ? null : (type.IsArray?type.GetElementType(): TypeModel.GetListItemType(model, type));
            if (itemType != null)
            {

                if (surrogate != null)
                {
                    throw new ArgumentException("Repeated data (a list, collection, etc) has inbuilt behaviour and cannot use a surrogate");
                }

                Type defaultType = null;
                ResolveListTypes(model, type, ref itemType, ref defaultType);

                //    Type nestedItemType = null;
                //Type nestedDefaultType = null;
                //MetaType.ResolveListTypes(model, itemType, ref nestedItemType, ref nestedDefaultType);

                return (IProtoTypeSerializer)
                       ValueMember.BuildValueFinalSerializer(
                           type,
                           new ValueMember.CollectionSettings(itemType)
                           {
                               Append = false,
                               DefaultType = CollectionConcreteType,
                               IsPacked = PrefixLength,
                               ReturnList = true
                           },
                           false,
                           AsReferenceDefault,
                           CollectionDataFormat,
                           false, // #1
                           null,
                           model);

            }

            // #2

            if (surrogate != null)
            {
                MetaType mt = model[surrogate], mtBase;
                while ((mtBase = mt.baseType) != null) { mt = mtBase; }
                return new SurrogateSerializer(model, type, surrogate, mt.Serializer);
            }
            if (IsAutoTuple)
            {
                MemberInfo[] mapping;
                ConstructorInfo ctor = ResolveTupleConstructor(type, out mapping);
                if(ctor == null) throw new InvalidOperationException();
                return new TupleSerializer(model, ctor, mapping, PrefixLength);
            }
            

            fields.Trim();
            int fieldCount = fields.Count;
            int subTypeCount = subTypes == null ? 0 : subTypes.Count;
            int[] fieldNumbers = new int[fieldCount + subTypeCount];
            IProtoSerializerWithWireType[] serializers = new IProtoSerializerWithWireType[fieldCount + subTypeCount];
            int i = 0;
            if (subTypeCount != 0)
            {
                foreach (SubType subType in subTypes)
                {
#if WINRT
                    if (!subType.DerivedType.IgnoreListHandling && ienumerable.IsAssignableFrom(subType.DerivedType.Type.GetTypeInfo()))
#else
                    if (!subType.DerivedType.IgnoreListHandling && model.MapType(ienumerable).IsAssignableFrom(subType.DerivedType.Type))
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
                foreach (ValueMember member in fields)
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
            return new TypeSerializer(model, type, fieldNumbers, serializers, arr, baseType == null, UseConstructor, callbacks, constructType, factory, PrefixLength);
        }

        [Flags]
        public enum AttributeFamily
        {
            None = 0, ProtoBuf = 1, DataContractSerialier = 2, XmlSerializer = 4, AutoTuple = 8, Aqla = 16, ImplicitFallback = 32, SystemSerializable = 64
        }
        
        public Type GetBaseType()
        {
#if WINRT
            return typeInfo.BaseType;
#else
            return type.BaseType;
#endif
        }

        public static ConstructorInfo ResolveTupleConstructor(Type type, out MemberInfo[] mappedMembers)
        {
            mappedMembers = null;
            if (type == null) throw new ArgumentNullException("type");
#if WINRT
            TypeInfo typeInfo = type.GetTypeInfo();
            if (typeInfo.IsAbstract) return null; // as if!
            ConstructorInfo[] ctors = Helpers.GetConstructors(typeInfo, false);
#else
            if (type.IsAbstract) return null; // as if!
            ConstructorInfo[] ctors = Helpers.GetConstructors(type, false);
#endif
            // need to have an interesting constructor to bother even checking this stuff
            if (ctors.Length == 0 || (ctors.Length == 1 && ctors[0].GetParameters().Length == 0)) return null;

            MemberInfo[] fieldsPropsUnfiltered = Helpers.GetInstanceFieldsAndProperties(type, true);
            BasicList memberList = new BasicList();
            for (int i = 0; i < fieldsPropsUnfiltered.Length; i++)
            {
                PropertyInfo prop = fieldsPropsUnfiltered[i] as PropertyInfo;
                if (prop != null)
                {
                    if (!prop.CanRead) return null; // no use if can't read
                    if (prop.CanWrite && Helpers.GetSetMethod(prop, false, false) != null) return null; // don't allow a public set (need to allow non-public to handle Mono's KeyValuePair<,>)
                    memberList.Add(prop);
                }
                else
                {
                    FieldInfo field = fieldsPropsUnfiltered[i] as FieldInfo;
                    if (field != null)
                    {
                        if (!field.IsInitOnly) return null; // all public fields must be readonly to be counted a tuple
                        memberList.Add(field);
                    }
                }
            }
            if (memberList.Count == 0)
            {
                return null;
            }

            MemberInfo[] members = new MemberInfo[memberList.Count];
            memberList.CopyTo(members, 0);

            int[] mapping = new int[members.Length];
            int found = 0;
            ConstructorInfo result = null;
            mappedMembers = new MemberInfo[mapping.Length];
            for (int i = 0; i < ctors.Length; i++)
            {
                ParameterInfo[] parameters = ctors[i].GetParameters();

                if (parameters.Length != members.Length) continue;

                // reset the mappings to test
                for (int j = 0; j < mapping.Length; j++) mapping[j] = -1;

                for (int j = 0; j < parameters.Length; j++)
                {
                    for(int k = 0 ; k < members.Length ; k++)
                    {
                        if (string.Compare(parameters[j].Name, members[k].Name, StringComparison.OrdinalIgnoreCase) != 0) continue;
                        Type memberType = Helpers.GetMemberType(members[k]);
                        if (memberType != parameters[j].ParameterType) continue;

                        mapping[j] = k;
                    }
                }
                // did we map all?
                bool notMapped = false;
                for (int j = 0; j < mapping.Length; j++)
                {
                    if (mapping[j] < 0)
                    {
                        notMapped = true;
                        break;
                    }
                    mappedMembers[j] = members[mapping[j]];
                }

                if (notMapped) continue;
                found++;
                result = ctors[i];

            }
            return found == 1 ? result : null;
        }

        /// <summary>
        /// Adds a member (by name) to the MetaType
        /// </summary>        
        public MetaType Add(int fieldNumber, string memberName)
        {
            AddField(fieldNumber, memberName, null, null, null);
            return this;
        }
        /// <summary>
        /// Adds a member (by name) to the MetaType, returning the ValueMember rather than the fluent API.
        /// This is otherwise identical to Add.
        /// </summary>
        public ValueMember AddField(int fieldNumber, string memberName)
        {
            return AddField(fieldNumber, memberName, null, null, null);
        }
        /// <summary>
        /// Gets or sets whether the type should use a parameterless constructor (the default),
        /// or whether the type should skip the constructor completely. This option is not supported
        /// on compact-framework.
        /// </summary>
        public bool UseConstructor
        { // negated to have defaults as flat zero
            get { return !HasFlag(OPTIONS_SkipConstructor); }
            set { SetFlag(OPTIONS_SkipConstructor, !value, true); }
        }
        /// <summary>
        /// The concrete type to create when a new instance of this type is needed; this may be useful when dealing
        /// with dynamic proxies, or with interface-based APIs
        /// </summary>
        public Type ConstructType
        {
            get { return constructType; }
            set
            {
                ThrowIfFrozen();
                constructType = value;
            }
        }
        private Type constructType;
        /// <summary>
        /// Adds a member (by name) to the MetaType
        /// </summary>     
        public MetaType Add(string memberName)
        {
            Add(GetNextFieldNumber(), memberName);
            return this;
        }
        Type surrogate;
        /// <summary>
        /// Performs serialization of this type via a surrogate; all
        /// other serialization options are ignored and handled
        /// by the surrogate's configuration.
        /// </summary>
        public void SetSurrogate(Type surrogateType)
        {
            if (surrogateType == type) surrogateType = null;
            if (surrogateType != null)
            {
                // note that BuildSerializer checks the **CURRENT TYPE** is OK to be surrogated
                if (surrogateType != null && Helpers.IsAssignableFrom(model.MapType(typeof(IEnumerable)), surrogateType))
                {
                    throw new ArgumentException("Repeated data (a list, collection, etc) has inbuilt behaviour and cannot be used as a surrogate");
                }
            }
            ThrowIfFrozen();
            this.surrogate = surrogateType;
            // no point in offering chaining; no options are respected
        }

        internal MetaType GetSurrogateOrSelf()
        {
            if (surrogate != null) return model[surrogate];
            return this;
        }
        internal MetaType GetSurrogateOrBaseOrSelf(bool deep) {
            if(surrogate != null) return model[surrogate];
            MetaType snapshot = this.baseType;
            if (snapshot != null)
            {
                if (deep)
                {
                    MetaType tmp;
                    do
                    {
                        tmp = snapshot;
                        snapshot = snapshot.baseType;
                    } while(snapshot != null);
                    return tmp;
                }
                return snapshot;
            }
            return this;
        }
        
        private int GetNextFieldNumber()
        {
            int maxField = 0;
            foreach (ValueMember member in fields)
            {
                if (member.FieldNumber > maxField) maxField = member.FieldNumber;
            }
            if (subTypes != null)
            {
                foreach (SubType subType in subTypes)
                {
                    if (subType.FieldNumber > maxField) maxField = subType.FieldNumber;
                }
            }
            return maxField + 1;
        }
        /// <summary>
        /// Adds a set of members (by name) to the MetaType
        /// </summary>     
        public MetaType Add(params string[] memberNames)
        {
            if (memberNames == null) throw new ArgumentNullException("memberNames");
            int next = GetNextFieldNumber();
            for (int i = 0; i < memberNames.Length; i++)
            {
                Add(next++, memberNames[i]);
            }
            return this;
        }


        /// <summary>
        /// Adds a member (by name) to the MetaType
        /// </summary>        
        public MetaType Add(int fieldNumber, string memberName, object defaultValue)
        {
            AddField(fieldNumber, memberName, null, null, defaultValue);
            return this;
        }

        /// <summary>
        /// Adds a member (by name) to the MetaType, including an itemType and defaultType for representing lists
        /// </summary>
        public MetaType Add(int fieldNumber, string memberName, Type itemType, Type defaultType)
        {
            AddField(fieldNumber, memberName, itemType, defaultType, null);
            return this;
        }

        /// <summary>
        /// Adds a member (by name) to the MetaType, including an itemType and defaultType for representing lists, returning the ValueMember rather than the fluent API.
        /// This is otherwise identical to Add.
        /// </summary>
        public ValueMember AddField(int fieldNumber, string memberName, Type itemType, Type defaultType)
        {
            return AddField(fieldNumber, memberName, itemType, defaultType, null);
        }
        
        private ValueMember AddField(int fieldNumber, string memberName, Type itemType, Type defaultType, object defaultValue)
        {
            if (type.IsArray) throw new InvalidOperationException("Can't add fields to array type");
            MemberInfo mi = null;
#if WINRT
            mi = Helpers.IsEnum(type) ? type.GetTypeInfo().GetDeclaredField(memberName) : Helpers.GetInstanceMember(type.GetTypeInfo(), memberName);

#else
            MemberInfo[] members = type.GetMember(memberName, Helpers.IsEnum(type) ? BindingFlags.Static | BindingFlags.Public : BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if(members != null && members.Length == 1) mi = members[0];
#endif
            if (mi == null) throw new ArgumentException("Unable to determine member: " + memberName, "memberName");

            Type miType;
#if WINRT || PORTABLE
            PropertyInfo pi = mi as PropertyInfo;
            if (pi == null)
            {
                FieldInfo fi = mi as FieldInfo;
                if (fi == null)
                {
                    throw new NotSupportedException(mi.GetType().Name);
                }
                else
                {
                    miType = fi.FieldType;
                }
            }
            else
            {
                miType = pi.PropertyType;
            }
#else   
            switch (mi.MemberType)
            {
                case MemberTypes.Field:
                    miType = ((FieldInfo)mi).FieldType; break;
                case MemberTypes.Property:
                    miType = ((PropertyInfo)mi).PropertyType; break;
                default:
                    throw new NotSupportedException(mi.MemberType.ToString());
            }
#endif
            ResolveListTypes(model, miType, ref itemType, ref defaultType);
            ValueMember newField = new ValueMember(model, type, fieldNumber, mi, miType, itemType, defaultType, BinaryDataFormat.Default, defaultValue);
            Add(newField);
            return newField;
        }

        internal static void ResolveListTypes(RuntimeTypeModel model, Type type, ref Type itemType, ref Type defaultType)
        {
            if (type == null) return;
            if (Helpers.GetTypeCode(type) != ProtoTypeCode.Unknown) return; // don't try this[type] for inbuilts
            if (model.AutoAddStrategy.GetIgnoreListHandling(type)) return;
            // handle arrays
            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1)
                {
                    throw new NotSupportedException("Multi-dimension arrays are supported");
                }
                itemType = type.GetElementType();
                if (itemType == model.MapType(typeof(byte)))
                {
                    defaultType = itemType = null;
                }
                else
                {
                    defaultType = type;
                }
            }
            // handle lists
            if (itemType == null) { itemType = TypeModel.GetListItemType(model, type); }
            
            if (itemType != null && defaultType == null)
            {
#if WINRT
                TypeInfo typeInfo = type.GetTypeInfo();
                if (typeInfo.IsClass && !typeInfo.IsAbstract && Helpers.GetConstructor(typeInfo, Helpers.EmptyTypes, true) != null)
#else
                if (type.IsClass && !type.IsAbstract && Helpers.GetConstructor(type, Helpers.EmptyTypes, true) != null)
#endif
                {
                    defaultType = type;
                }
                if (defaultType == null)
                {
#if WINRT
                    if (typeInfo.IsInterface)
#else
                    if (type.IsInterface)
#endif
                    {
#if NO_GENERICS
                        defaultType = typeof(ArrayList);
#else
                        Type[] genArgs;
#if WINRT
                        if (typeInfo.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IDictionary<,>)
                            && itemType == typeof(System.Collections.Generic.KeyValuePair<,>).MakeGenericType(genArgs = typeInfo.GenericTypeArguments))
#else
                        if (type.IsGenericType && type.GetGenericTypeDefinition() == model.MapType(typeof(System.Collections.Generic.IDictionary<,>))
                            && itemType == model.MapType(typeof(System.Collections.Generic.KeyValuePair<,>)).MakeGenericType(genArgs = type.GetGenericArguments()))
#endif
                        {
                            defaultType = model.MapType(typeof(System.Collections.Generic.Dictionary<,>)).MakeGenericType(genArgs);
                        }
                        else
                        {
                            defaultType = model.MapType(typeof(System.Collections.Generic.List<>)).MakeGenericType(itemType);
                        }
#endif
                    }
                }
                // verify that the default type is appropriate
                if (defaultType != null && !Helpers.IsAssignableFrom(type, defaultType)) { defaultType = null; }
            }
        }

        public static bool IsDictionaryOrListInterface(RuntimeTypeModel model, Type type, out Type defaultType)
        {
            defaultType = null;
            if (!Helpers.IsInterface(type)) return false;
            Type[] genArgs;
            var itemType = TypeModel.GetListItemType(model, type);
#if WINRT
            TypeInfo typeInfo = type.GetTypeInfo();
            if (typeInfo.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IDictionary<,>)
                && itemType == typeof(System.Collections.Generic.KeyValuePair<,>).MakeGenericType(genArgs = typeInfo.GenericTypeArguments))
#else
            if (type.IsGenericType && type.GetGenericTypeDefinition() == model.MapType(typeof(System.Collections.Generic.IDictionary<,>))
                    && itemType == model.MapType(typeof(System.Collections.Generic.KeyValuePair<,>)).MakeGenericType(genArgs = type.GetGenericArguments()))
#endif
            {
                defaultType = model.MapType(typeof(System.Collections.Generic.Dictionary<,>)).MakeGenericType(genArgs);
                return true;
            }
#if WINRT
            if (typeInfo.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IList<>).MakeGenericType(genArgs = typeInfo.GenericTypeArguments))
#else
            if (type.IsGenericType && type.GetGenericTypeDefinition() == model.MapType(typeof(System.Collections.Generic.IList<>)).MakeGenericType(genArgs = type.GetGenericArguments()))
#endif
            {
                defaultType = model.MapType(typeof(System.Collections.Generic.List<>)).MakeGenericType(genArgs);
                return true;
            }
            return false;

        }

        public void Add(SerializableMemberAttribute normalizedAttribute, MemberInfo member, object defaultValue)
        {
            Add(normalizedAttribute, member, defaultValue, null);
        }

        public void Add(SerializableMemberAttribute normalizedAttribute, MemberInfo member, object defaultValue, Type defaultType)
        {
            Add(normalizedAttribute, member, true, defaultValue, defaultType);
        }

        public void Add(SerializableMemberAttribute normalizedAttribute, MemberInfo member)
        {
            Add(normalizedAttribute, member, null);
        }

        public void Add(SerializableMemberAttribute normalizedAttribute, MemberInfo member, Type defaultType)
        {
            Add(normalizedAttribute, member, false, null, defaultType);
        }

        private void Add(SerializableMemberAttribute normalizedAttribute, MemberInfo member, bool defaultValueSpecified, object defaultValue)
        {
            Add(normalizedAttribute, member, defaultValueSpecified, defaultValue, null);
        }

        void Add(SerializableMemberAttribute normalizedAttribute, MemberInfo member, bool defaultValueSpecified, object defaultValue, Type defaultType)
        {
            Type effectiveType = Helpers.GetMemberType(member);
            
            // implicit zero default
            if (!defaultValueSpecified)
            {
                defaultValue = null;
                if (model.UseImplicitZeroDefaults)
                {
                    switch (Helpers.GetTypeCode(effectiveType))
                    {
                        case ProtoTypeCode.Boolean: defaultValue = false; break;
                        case ProtoTypeCode.Decimal: defaultValue = (decimal)0; break;
                        case ProtoTypeCode.Single: defaultValue = (float)0; break;
                        case ProtoTypeCode.Double: defaultValue = (double)0; break;
                        case ProtoTypeCode.Byte: defaultValue = (byte)0; break;
                        case ProtoTypeCode.Char: defaultValue = (char)0; break;
                        case ProtoTypeCode.Int16: defaultValue = (short)0; break;
                        case ProtoTypeCode.Int32: defaultValue = (int)0; break;
                        case ProtoTypeCode.Int64: defaultValue = (long)0; break;
                        case ProtoTypeCode.SByte: defaultValue = (sbyte)0; break;
                        case ProtoTypeCode.UInt16: defaultValue = (ushort)0; break;
                        case ProtoTypeCode.UInt32: defaultValue = (uint)0; break;
                        case ProtoTypeCode.UInt64: defaultValue = (ulong)0; break;
                        case ProtoTypeCode.TimeSpan: defaultValue = TimeSpan.Zero; break;
                        case ProtoTypeCode.Guid: defaultValue = Guid.Empty; break;
                    }
                }
            }

            Type itemType = null;
            
            Type t = null;

            // check for list types
            ResolveListTypes(model, effectiveType, ref itemType, ref t);
            // but take it back if it is explicitly excluded
            if (itemType != null)
            { // looks like a list, but double check for IgnoreListHandling
                int idx = model.FindOrAddAuto(effectiveType, false, true, false);
                if (idx >= 0 && model[effectiveType].IgnoreListHandling)
                {
                    itemType = null;
                    t = null;
                }
            }

            if (t != null)
            {
                if (defaultType != t && defaultType != null)
                    throw new ArgumentException("Specified defaultType " + defaultType.Name + " can't be used because found default list type " + t.Name);
                defaultType = t;
            }
            else if (defaultType != null && !Helpers.IsAssignableFrom(effectiveType, defaultType))
                throw new ProtoException("Specified default type " + defaultType.Name + " is not assignable to member " + this.Type.Name + "." + member.Name);
            

            var vm = new ValueMember(model, this.Type, normalizedAttribute.Tag, member, effectiveType, itemType, defaultType, normalizedAttribute.DataFormat, defaultValue);
#if WINRT
                TypeInfo finalType = typeInfo;
#else
            Type finalType = this.Type;
#endif
            PropertyInfo prop = Helpers.GetProperty(finalType, member.Name + "Specified", true);
            MethodInfo getMethod = Helpers.GetGetMethod(prop, true, true);
            if (getMethod == null || getMethod.IsStatic) prop = null;
            if (prop != null)
            {
                vm.SetSpecified(getMethod, Helpers.GetSetMethod(prop, true, true));
            }
            else
            {
                MethodInfo method = Helpers.GetInstanceMethod(finalType, "ShouldSerialize" + member.Name, Helpers.EmptyTypes);
                if (method != null && method.ReturnType == model.MapType(typeof(bool)))
                {
                    vm.SetSpecified(method, null);
                }
            }
            if (!Helpers.IsNullOrEmpty(normalizedAttribute.Name)) vm.SetName(normalizedAttribute.Name);
            vm.IsPacked = normalizedAttribute.IsPacked;
            vm.IsRequired = normalizedAttribute.IsRequired;
            vm.AppendCollection = normalizedAttribute.AppendCollection;
            vm.AsReference = true;
            if (normalizedAttribute.NotAsReferenceHasValue)
                vm.AsReference = !normalizedAttribute.NotAsReference;

            vm.DynamicType = normalizedAttribute.DynamicType;
            Add(vm);
        }

        private void Add(ValueMember member) {
            int opaqueToken = 0;
            try {
                model.TakeLock(ref opaqueToken);
                ThrowIfFrozen();
                fields.Add(member);
            } finally
            {
                model.ReleaseLock(opaqueToken);
            }
        }
        /// <summary>
        /// Returns the ValueMember that matchs a given field number, or null if not found
        /// </summary>
        public ValueMember this[int fieldNumber]
        {
            get
            {
                foreach (ValueMember member in fields)
                {
                    if (member.FieldNumber == fieldNumber) return member;
                }
                return null;
            }
        }
        /// <summary>
        /// Returns the ValueMember that matchs a given member (property/field), or null if not found
        /// </summary>
        public ValueMember this[MemberInfo member]
        {
            get
            {
                if (member == null) return null;
                foreach (ValueMember x in fields)
                {
                    if (x.Member == member) return x;
                }
                return null;
            }
        }
        private readonly BasicList fields = new BasicList();

        /// <summary>
        /// Returns the ValueMember instances associated with this type
        /// </summary>
        public ValueMember[] GetFields() {
            ValueMember[] arr = new ValueMember[fields.Count];
            fields.CopyTo(arr, 0);
            Array.Sort(arr, ValueMember.Comparer.Default);
            return arr;
        }

        /// <summary>
        /// Returns the SubType instances associated with this type
        /// </summary>
        public SubType[] GetSubtypes()
        {
            if (subTypes == null || subTypes.Count == 0) return new SubType[0];
            SubType[] arr = new SubType[subTypes.Count];
            subTypes.CopyTo(arr, 0);
            Array.Sort(arr, SubType.Comparer.Default);
            return arr;
        }

#if FEAT_COMPILER && !FX11

        /// <summary>
        /// Compiles the serializer for this type; this is *not* a full
        /// standalone compile, but can significantly boost performance
        /// while allowing additional types to be added.
        /// </summary>
        /// <remarks>An in-place compile can access non-public types / members</remarks>
        public void CompileInPlace()
        {
            // TODO temporarily disabled
            return;
#if FEAT_IKVM
            // just no nothing, quietely; don't want to break the API
#else
            serializer = CompiledSerializer.Wrap(Serializer, model);
            rootSerializer = CompiledSerializer.Wrap(RootSerializer, model);
#endif
        }
#endif

        internal bool IsDefined(int fieldNumber)
        {
            foreach (ValueMember field in fields)
            {
                if (field.FieldNumber == fieldNumber) return true;
            }
            return false;
        }

        internal int GetKey(bool demand, bool getBaseKey)
        {
            return model.GetKey(type, demand, getBaseKey);
        }



        internal EnumSerializer.EnumPair[] GetEnumMap()
        {
            if (HasFlag(OPTIONS_EnumPassThru)) return null;
            EnumSerializer.EnumPair[] result = new EnumSerializer.EnumPair[fields.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ValueMember member = (ValueMember) fields[i];
                int wireValue = member.FieldNumber;
                object value = member.GetRawEnumValue();
                result[i] = new EnumSerializer.EnumPair(wireValue, value, member.MemberType);
            }
            return result;
        }


        /// <summary>
        /// Gets or sets a value indicating that an enum should be treated directly as an int/short/etc, rather
        /// than enforcing .proto enum rules. This is useful *in particul* for [Flags] enums.
        /// </summary>
        public bool EnumPassthru
        {
            get { return HasFlag(OPTIONS_EnumPassThru); }
            set { SetFlag(OPTIONS_EnumPassThru, value, true); }
        }

        /// <summary>
        /// Gets or sets a value indicating that this type should NOT be treated as a list, even if it has
        /// familiar list-like characteristics (enumerable, add, etc)
        /// </summary>
        public bool IgnoreListHandling
        {
            get { return HasFlag(OPTIONS_IgnoreListHandling); }
            set
            {
                if (value && type.IsArray)
                    throw new InvalidOperationException("Can't disable list handling for arrays");
                SetFlag(OPTIONS_IgnoreListHandling, value, true);
            }
        }

        internal bool Pending
        {
            get { return HasFlag(OPTIONS_Pending); }
            set { SetFlag(OPTIONS_Pending, value, false); }
        }

        private const byte
            OPTIONS_Pending = 1,
            OPTIONS_EnumPassThru = 2,
            OPTIONS_Frozen = 4,
            OPTIONS_PrivateOnApi = 8,
            OPTIONS_SkipConstructor = 16,
            OPTIONS_AsReferenceDefault = 32,
            OPTIONS_AutoTuple = 64,
            OPTIONS_IgnoreListHandling = 128;

        private volatile byte flags;
        private bool HasFlag(byte flag) { return (flags & flag) == flag; }
        private void SetFlag(byte flag, bool value, bool throwIfFrozen)
        {
            if (throwIfFrozen && HasFlag(flag) != value)
            {
                ThrowIfFrozen();
            }
            if (value)
                flags |= flag;
            else
                flags = (byte)(flags & ~flag);
        }

        internal static MetaType GetRootType(MetaType source)
        {
           
            while (source.serializer != null)
            {
                MetaType tmp = source.baseType;
                if (tmp == null) return source;
                source = tmp; // else loop until we reach something that isn't generated, or is the root
            }

            // now we get into uncertain territory
            RuntimeTypeModel model = source.model;
            int opaqueToken = 0;
            try {
                model.TakeLock(ref opaqueToken);

                MetaType tmp;
                while ((tmp = source.baseType) != null) source = tmp;
                return source;

            } finally {
                model.ReleaseLock(opaqueToken);
            }
        }

        internal bool IsPrepared()
        {
            #if FEAT_COMPILER && !FEAT_IKVM && !FX11
            return serializer is CompiledSerializer;
            #else
            return false;
            #endif
        }

        internal System.Collections.IEnumerable Fields { get { return this.fields; } }

        internal static System.Text.StringBuilder NewLine(System.Text.StringBuilder builder, int indent)
        {
            return Helpers.AppendLine(builder).Append(' ', indent*3);
        }
        internal bool IsAutoTuple
        {
            get { return HasFlag(OPTIONS_AutoTuple); }
            set { SetFlag(OPTIONS_AutoTuple, value, true); }
        }
        internal void WriteSchema(System.Text.StringBuilder builder, int indent, ref bool requiresBclImport)
        {
            if (surrogate != null) return; // nothing to write


            ValueMember[] fieldsArr = new ValueMember[fields.Count];
            fields.CopyTo(fieldsArr, 0);
            Array.Sort(fieldsArr, ValueMember.Comparer.Default);

            if (IsList)
            {
                string itemTypeName = model.GetSchemaTypeName(TypeModel.GetListItemType(model, type), BinaryDataFormat.Default, false, false, ref requiresBclImport);
                NewLine(builder, indent).Append("message ").Append(GetSchemaTypeName()).Append(" {");
                NewLine(builder, indent + 1).Append("repeated ").Append(itemTypeName).Append(" items = 1;");
                NewLine(builder, indent).Append('}');
            }
            else if (IsAutoTuple)
            { // key-value-pair etc
                MemberInfo[] mapping;
                if(ResolveTupleConstructor(type, out mapping) != null)
                {
                    NewLine(builder, indent).Append("message ").Append(GetSchemaTypeName()).Append(" {");
                    for(int i = 0 ; i < mapping.Length ; i++)
                    {
                        Type effectiveType;
                        if(mapping[i] is PropertyInfo)
                        {
                            effectiveType = ((PropertyInfo) mapping[i]).PropertyType;
                        } else if (mapping[i] is FieldInfo)
                        {
                            effectiveType = ((FieldInfo) mapping[i]).FieldType;
                        } else
                        {
                            throw new NotSupportedException("Unknown member type: " + mapping[i].GetType().Name);
                        }
                        NewLine(builder, indent + 1).Append("optional ").Append(model.GetSchemaTypeName(effectiveType, BinaryDataFormat.Default, false, false, ref requiresBclImport).Replace('.','_'))
                            .Append(' ').Append(mapping[i].Name).Append(" = ").Append(i + 1).Append(';');
                    }
                    NewLine(builder, indent).Append('}');
                }
            }
            else if(Helpers.IsEnum(type))
            {
                NewLine(builder, indent).Append("enum ").Append(GetSchemaTypeName()).Append(" {");
                if (fieldsArr.Length == 0 && EnumPassthru) {
                    if (type
#if WINRT
                    .GetTypeInfo()
#endif
.IsDefined(model.MapType(typeof(FlagsAttribute)), false))
                    {
                        NewLine(builder, indent + 1).Append("// this is a composite/flags enumeration");
                    }
                    else
                    {
                        NewLine(builder, indent + 1).Append("// this enumeration will be passed as a raw value");
                    }
                    foreach(FieldInfo field in
#if WINRT
                        type.GetRuntimeFields()
#else
                        type.GetFields()
#endif
                        
                        )
                    {
                        if(field.IsStatic && field.IsLiteral)
                        {
                            object enumVal;
#if WINRT || PORTABLE || CF || FX11
                            enumVal = field.GetValue(null);
#else
                            enumVal = field.GetRawConstantValue();
#endif
                            NewLine(builder, indent + 1).Append(field.Name).Append(" = ").Append(enumVal).Append(";");
                        }
                    }
                    
                }
                else
                {
                    foreach (ValueMember member in fieldsArr)
                    {
                        NewLine(builder, indent + 1).Append(member.Name).Append(" = ").Append(member.FieldNumber).Append(';');
                    }
                }
                NewLine(builder, indent).Append('}');
            } else
            {
                NewLine(builder, indent).Append("message ").Append(GetSchemaTypeName()).Append(" {");
                foreach (ValueMember member in fieldsArr)
                {
                    string ordinality = member.ItemType != null ? "repeated" : member.IsRequired ? "required" : "optional";
                    NewLine(builder, indent + 1).Append(ordinality).Append(' ');
                    if (member.DataFormat == BinaryDataFormat.Group) builder.Append("group ");
                    string schemaTypeName = member.GetSchemaTypeName(true, ref requiresBclImport);
                    builder.Append(schemaTypeName).Append(" ")
                         .Append(member.Name).Append(" = ").Append(member.FieldNumber);

                    if(member.DefaultValue != null && member.IsRequired == false)
                    {
                        if (member.DefaultValue is string)
                        {
                            builder.Append(" [default = \"").Append(member.DefaultValue).Append("\"]");
                        }
                        else if(member.DefaultValue is bool)
                        {   // need to be lower case (issue 304)
                            builder.Append((bool)member.DefaultValue ? " [default = true]" : " [default = false]");
                        }
                        else
                        {
                            builder.Append(" [default = ").Append(member.DefaultValue).Append(']');
                        }
                    }
                    if(member.ItemType != null && member.IsPacked)
                    {
                        builder.Append(" [packed=true]");
                    }
                    builder.Append(';');
                    if (schemaTypeName == "bcl.NetObjectProxy" && member.AsReference && !member.DynamicType) // we know what it is; tell the user
                    {
                        builder.Append(" // reference-tracked ").Append(member.GetSchemaTypeName(false, ref requiresBclImport));
                    }
                }
                if (subTypes != null && subTypes.Count != 0)
                {
                    NewLine(builder, indent + 1).Append("// the following represent sub-types; at most 1 should have a value");
                    SubType[] subTypeArr = new SubType[subTypes.Count];
                    subTypes.CopyTo(subTypeArr, 0);
                    Array.Sort(subTypeArr, SubType.Comparer.Default);
                    foreach (SubType subType in subTypeArr)
                    {
                        string subTypeName = subType.DerivedType.GetSchemaTypeName();
                        NewLine(builder, indent + 1).Append("optional ").Append(subTypeName)
                            .Append(" ").Append(subTypeName).Append(" = ").Append(subType.FieldNumber).Append(';');
                    }
                }
                NewLine(builder, indent).Append('}');
            }
        }
    }
}
#endif
