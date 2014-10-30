﻿// Used protobuf-net source code modified by Vladyslav Taranov for AqlaSerializer, 2014
using System;

#if FEAT_IKVM
using ProtoBuf;
using Type = IKVM.Reflection.Type;
using IKVM.Reflection;
#else
using System.Reflection;
using ProtoBuf;

#endif

namespace AqlaSerializer
{
    /// <summary>
    /// Declares a member to be used in protocol-buffer serialization, using
    /// the given Tag. A DataFormat may be used to optimise the serialization
    /// format (for instance, using zigzag encoding for negative numbers, or 
    /// fixed-length encoding for large values.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
        AllowMultiple = false, Inherited = true)]
    public class SerializableMemberAttribute : Attribute
        , IComparable
#if !NO_GENERICS
, IComparable<SerializableMemberAttribute>
#endif

    {
        /// <summary>
        /// Compare with another ProtoMemberAttribute for sorting purposes
        /// </summary>
        public int CompareTo(object other) { return CompareTo(other as SerializableMemberAttribute); }
        /// <summary>
        /// Compare with another ProtoMemberAttribute for sorting purposes
        /// </summary>
        public int CompareTo(SerializableMemberAttribute other)
        {
            if (other == null) return -1;
            if ((object)this == (object)other) return 0;
            int result = this.tag.CompareTo(other.tag);
            if (result == 0) result = string.CompareOrdinal(this.name, other.name);
            return result;
        }

        /// <summary>
        /// Creates a new ProtoMemberAttribute instance.
        /// </summary>
        /// <param name="tag">Specifies the unique tag used to identify this member within the type.</param>
        public SerializableMemberAttribute(int tag)
            : this(tag, false)
        { }

        internal SerializableMemberAttribute(int tag, bool forced)
        {
            if (tag <= 0 && !forced) throw new ArgumentOutOfRangeException("tag");
            this.tag = tag;
        }

#if !NO_RUNTIME
        internal MemberInfo Member;
        internal bool TagIsPinned;
#endif
        /// <summary>
        /// Gets or sets the original name defined in the .proto; not used
        /// during serialization.
        /// </summary>
        public string Name { get { return name; } set { name = value; } }
        private string name;

        /// <summary>
        /// Gets or sets the data-format to be used when encoding this value.
        /// </summary>
        public DataFormat DataFormat { get { return dataFormat; } set { dataFormat = value; } }
        private DataFormat dataFormat;

        /// <summary>
        /// Gets the unique tag used to identify this member within the type.
        /// </summary>
        public int Tag { get { return tag; } }
        private int tag;
        internal void Rebase(int tag) { this.tag = tag; }

        /// <summary>
        /// Gets or sets a value indicating whether this member is mandatory.
        /// </summary>
        public bool IsRequired
        {
            get { return (options & MemberSerializationOptions.Required) == MemberSerializationOptions.Required; }
            set
            {
                if (value) options |= MemberSerializationOptions.Required;
                else options &= ~MemberSerializationOptions.Required;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this member is packed.
        /// This option only applies to list/array data of primitive types (int, double, etc).
        /// </summary>
        public bool IsPacked
        {
            get { return (options & MemberSerializationOptions.Packed) == MemberSerializationOptions.Packed; }
            set
            {
                if (value) options |= MemberSerializationOptions.Packed;
                else options &= ~MemberSerializationOptions.Packed;
            }
        }

        /// <summary>
        /// Indicates whether this field should *append* to existing values (the default is true, meaning *replace*).
        /// This option only applies to list/array data.
        /// </summary>
        public bool AppendCollection
        {
            get { return (options & MemberSerializationOptions.AppendCollection) == MemberSerializationOptions.AppendCollection; }
            set
            {
                if (value) options |= MemberSerializationOptions.AppendCollection;
                else options &= ~MemberSerializationOptions.AppendCollection;
            }
        }

        /// <summary>
        /// Enables full object-tracking/full-graph support.
        /// </summary>
        [Obsolete("In AqlaSerializer use NotAsReference")]
        public bool AsReference
        {
            get { return !NotAsReference; }
            set
            {
                NotAsReference = !value;
            }
        }

        /// <summary>
        /// Enables full object-tracking/full-graph support.
        /// </summary>
        public bool NotAsReference
        {
            get { return (options & MemberSerializationOptions.NotAsReference) == MemberSerializationOptions.NotAsReference; }
            set
            {
                if (value) options |= MemberSerializationOptions.NotAsReference;
                else options &= ~MemberSerializationOptions.NotAsReference;

                options |= MemberSerializationOptions.NotAsReferenceHasValue;
            }
        }

        internal bool NotAsReferenceHasValue
        {
            get { return (options & MemberSerializationOptions.NotAsReferenceHasValue) == MemberSerializationOptions.NotAsReferenceHasValue; }
            set
            {
                if (value) options |= MemberSerializationOptions.NotAsReferenceHasValue;
                else options &= ~MemberSerializationOptions.NotAsReferenceHasValue;
            }
        }

        /// <summary>
        /// Embeds the type information into the stream, allowing usage with types not known in advance.
        /// </summary>
        public bool DynamicType
        {
            get { return (options & MemberSerializationOptions.DynamicType) == MemberSerializationOptions.DynamicType; }
            set
            {
                if (value) options |= MemberSerializationOptions.DynamicType;
                else options &= ~MemberSerializationOptions.DynamicType;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this member is packed (lists/arrays).
        /// </summary>
        public MemberSerializationOptions Options { get { return options; } set { options = value; } }
        private MemberSerializationOptions options;

        /// <summary>
        /// Additional (optional) settings that control serialization of members
        /// </summary>
        [Flags]
        public enum MemberSerializationOptions
        {
            /// <summary>
            /// Default; no additional options
            /// </summary>
            None = 0,
            /// <summary>
            /// Indicates that repeated elements should use packed (length-prefixed) encoding
            /// </summary>
            Packed = 1,
            /// <summary>
            /// Indicates that the given item is required
            /// </summary>
            Required = 2,
            /// <summary>
            /// Disable full object-tracking/full-graph support
            /// </summary>
            NotAsReference = 4,
            /// <summary>
            /// Embeds the type information into the stream, allowing usage with types not known in advance
            /// </summary>
            DynamicType = 8,
            /// <summary>
            /// Indicates whether this field should *repace* existing values (the default is false, meaning *append*).
            /// This option only applies to list/array data.
            /// </summary>
            AppendCollection = 16,
            /// <summary>
            /// Determines whether the types AsReferenceDefault value is used, or whether this member's AsReference should be used
            /// </summary>
            NotAsReferenceHasValue = 32
        }


    }

    /// <summary>
    /// Declares a member to be used in protocol-buffer serialization, using
    /// the given Tag and MemberName. This allows ProtoMemberAttribute usage
    /// even for partial classes where the individual members are not
    /// under direct control.
    /// A DataFormat may be used to optimise the serialization
    /// format (for instance, using zigzag encoding for negative numbers, or 
    /// fixed-length encoding for large values.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class,
            AllowMultiple = true, Inherited = false)]
    public sealed class SerializablePartialMemberAttribute : SerializableMemberAttribute
    {
        /// <summary>
        /// Creates a new ProtoMemberAttribute instance.
        /// </summary>
        /// <param name="tag">Specifies the unique tag used to identify this member within the type.</param>
        /// <param name="memberName">Specifies the member to be serialized.</param>
        public SerializablePartialMemberAttribute(int tag, string memberName)
            : base(tag)
        {
            if (Helpers.IsNullOrEmpty(memberName)) throw new ArgumentNullException("memberName");
            this.memberName = memberName;
        }
        /// <summary>
        /// The name of the member to be serialized.
        /// </summary>
        public string MemberName { get { return memberName; } }
        private readonly string memberName;
    }
}
