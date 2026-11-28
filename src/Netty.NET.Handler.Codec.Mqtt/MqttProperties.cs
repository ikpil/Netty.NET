/*
 * Copyright 2020 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * MQTT Properties container
 * */
// CLR: io.netty.util.collection.IntObjectHashMap is replaced by Dictionary; it exists because Java "Type arguments
// may be either reference types or wildcards"
// (https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.5.1), so a Map<Integer, V> boxes every key.
// A.NET generic type constructed with a value type gets "a specialized generic type" with the value "substituted"
// (https://learn.microsoft.com/dotnet/csharp/programming-guide/generics/generics-in-the-run-time), so the map is
// Dictionary<int, MqttProperty>. Its enumeration order "is undefined"
// (https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2), and IntObjectHashMap iterates
// in its hash-slot order; neither order is a documented contract, so listAll() and the encoded property order may
// differ from the JVM's. Map.get returns null for a missing key while the C# indexer throws, so lookups use
// TryGetValue. UnsupportedOperationException is NotSupportedException.
public sealed class MqttProperties
{

    // single byte properties
    public const int PAYLOAD_FORMAT_INDICATOR = 0x01;
    public const int REQUEST_PROBLEM_INFORMATION = 0x17;
    public const int REQUEST_RESPONSE_INFORMATION = 0x19;
    public const int MAXIMUM_QOS = 0x24;
    public const int RETAIN_AVAILABLE = 0x25;
    public const int WILDCARD_SUBSCRIPTION_AVAILABLE = 0x28;
    public const int SUBSCRIPTION_IDENTIFIER_AVAILABLE = 0x29;
    public const int SHARED_SUBSCRIPTION_AVAILABLE = 0x2A;

    // two bytes properties
    public const int SERVER_KEEP_ALIVE = 0x13;
    public const int RECEIVE_MAXIMUM = 0x21;
    public const int TOPIC_ALIAS_MAXIMUM = 0x22;
    public const int TOPIC_ALIAS = 0x23;

    // four bytes properties
    public const int PUBLICATION_EXPIRY_INTERVAL = 0x02;
    public const int SESSION_EXPIRY_INTERVAL = 0x11;
    public const int WILL_DELAY_INTERVAL = 0x18;
    public const int MAXIMUM_PACKET_SIZE = 0x27;

    // Variable Byte Integer
    public const int SUBSCRIPTION_IDENTIFIER = 0x0B;

    // UTF-8 Encoded String properties
    public const int CONTENT_TYPE = 0x03;
    public const int RESPONSE_TOPIC = 0x08;
    public const int ASSIGNED_CLIENT_IDENTIFIER = 0x12;
    public const int AUTHENTICATION_METHOD = 0x15;
    public const int RESPONSE_INFORMATION = 0x1A;
    public const int SERVER_REFERENCE = 0x1C;
    public const int REASON_STRING = 0x1F;
    public const int USER_PROPERTY = 0x26;

    // Binary Data
    public const int CORRELATION_DATA = 0x09;
    public const int AUTHENTICATION_DATA = 0x16;

    // CLR: @Deprecated without a @deprecated text names no replacement, so the enum is ported with [Obsolete];
    // ObsoleteAttribute "Marks program elements that are no longer in use"
    // (https://learn.microsoft.com/dotnet/api/system.obsoleteattribute). The enum with a field and methods is a
    // sealed class with static readonly constants; Name(), Values() and ToString() stand for the java.lang.Enum
    // members (see MqttQoS), and ArrayIndexOutOfBoundsException is IndexOutOfRangeException
    // (https://learn.microsoft.com/dotnet/api/system.indexoutofrangeexception).
    [Obsolete]
    public sealed class MqttPropertyType
    {
        // single byte properties
        public static readonly MqttPropertyType PAYLOAD_FORMAT_INDICATOR =
                new MqttPropertyType(nameof(PAYLOAD_FORMAT_INDICATOR), MqttProperties.PAYLOAD_FORMAT_INDICATOR);
        public static readonly MqttPropertyType REQUEST_PROBLEM_INFORMATION =
                new MqttPropertyType(nameof(REQUEST_PROBLEM_INFORMATION), MqttProperties.REQUEST_PROBLEM_INFORMATION);
        public static readonly MqttPropertyType REQUEST_RESPONSE_INFORMATION =
                new MqttPropertyType(nameof(REQUEST_RESPONSE_INFORMATION), MqttProperties.REQUEST_RESPONSE_INFORMATION);
        public static readonly MqttPropertyType MAXIMUM_QOS =
                new MqttPropertyType(nameof(MAXIMUM_QOS), MqttProperties.MAXIMUM_QOS);
        public static readonly MqttPropertyType RETAIN_AVAILABLE =
                new MqttPropertyType(nameof(RETAIN_AVAILABLE), MqttProperties.RETAIN_AVAILABLE);
        public static readonly MqttPropertyType WILDCARD_SUBSCRIPTION_AVAILABLE =
                new MqttPropertyType(nameof(WILDCARD_SUBSCRIPTION_AVAILABLE),
                        MqttProperties.WILDCARD_SUBSCRIPTION_AVAILABLE);
        public static readonly MqttPropertyType SUBSCRIPTION_IDENTIFIER_AVAILABLE =
                new MqttPropertyType(nameof(SUBSCRIPTION_IDENTIFIER_AVAILABLE),
                        MqttProperties.SUBSCRIPTION_IDENTIFIER_AVAILABLE);
        public static readonly MqttPropertyType SHARED_SUBSCRIPTION_AVAILABLE =
                new MqttPropertyType(nameof(SHARED_SUBSCRIPTION_AVAILABLE), MqttProperties.SHARED_SUBSCRIPTION_AVAILABLE);

        // two bytes properties
        public static readonly MqttPropertyType SERVER_KEEP_ALIVE =
                new MqttPropertyType(nameof(SERVER_KEEP_ALIVE), MqttProperties.SERVER_KEEP_ALIVE);
        public static readonly MqttPropertyType RECEIVE_MAXIMUM =
                new MqttPropertyType(nameof(RECEIVE_MAXIMUM), MqttProperties.RECEIVE_MAXIMUM);
        public static readonly MqttPropertyType TOPIC_ALIAS_MAXIMUM =
                new MqttPropertyType(nameof(TOPIC_ALIAS_MAXIMUM), MqttProperties.TOPIC_ALIAS_MAXIMUM);
        public static readonly MqttPropertyType TOPIC_ALIAS =
                new MqttPropertyType(nameof(TOPIC_ALIAS), MqttProperties.TOPIC_ALIAS);

        // four bytes properties
        public static readonly MqttPropertyType PUBLICATION_EXPIRY_INTERVAL =
                new MqttPropertyType(nameof(PUBLICATION_EXPIRY_INTERVAL), MqttProperties.PUBLICATION_EXPIRY_INTERVAL);
        public static readonly MqttPropertyType SESSION_EXPIRY_INTERVAL =
                new MqttPropertyType(nameof(SESSION_EXPIRY_INTERVAL), MqttProperties.SESSION_EXPIRY_INTERVAL);
        public static readonly MqttPropertyType WILL_DELAY_INTERVAL =
                new MqttPropertyType(nameof(WILL_DELAY_INTERVAL), MqttProperties.WILL_DELAY_INTERVAL);
        public static readonly MqttPropertyType MAXIMUM_PACKET_SIZE =
                new MqttPropertyType(nameof(MAXIMUM_PACKET_SIZE), MqttProperties.MAXIMUM_PACKET_SIZE);

        // Variable Byte Integer
        public static readonly MqttPropertyType SUBSCRIPTION_IDENTIFIER =
                new MqttPropertyType(nameof(SUBSCRIPTION_IDENTIFIER), MqttProperties.SUBSCRIPTION_IDENTIFIER);

        // UTF-8 Encoded String properties
        public static readonly MqttPropertyType CONTENT_TYPE =
                new MqttPropertyType(nameof(CONTENT_TYPE), MqttProperties.CONTENT_TYPE);
        public static readonly MqttPropertyType RESPONSE_TOPIC =
                new MqttPropertyType(nameof(RESPONSE_TOPIC), MqttProperties.RESPONSE_TOPIC);
        public static readonly MqttPropertyType ASSIGNED_CLIENT_IDENTIFIER =
                new MqttPropertyType(nameof(ASSIGNED_CLIENT_IDENTIFIER), MqttProperties.ASSIGNED_CLIENT_IDENTIFIER);
        public static readonly MqttPropertyType AUTHENTICATION_METHOD =
                new MqttPropertyType(nameof(AUTHENTICATION_METHOD), MqttProperties.AUTHENTICATION_METHOD);
        public static readonly MqttPropertyType RESPONSE_INFORMATION =
                new MqttPropertyType(nameof(RESPONSE_INFORMATION), MqttProperties.RESPONSE_INFORMATION);
        public static readonly MqttPropertyType SERVER_REFERENCE =
                new MqttPropertyType(nameof(SERVER_REFERENCE), MqttProperties.SERVER_REFERENCE);
        public static readonly MqttPropertyType REASON_STRING =
                new MqttPropertyType(nameof(REASON_STRING), MqttProperties.REASON_STRING);
        public static readonly MqttPropertyType USER_PROPERTY =
                new MqttPropertyType(nameof(USER_PROPERTY), MqttProperties.USER_PROPERTY);

        // Binary Data
        public static readonly MqttPropertyType CORRELATION_DATA =
                new MqttPropertyType(nameof(CORRELATION_DATA), MqttProperties.CORRELATION_DATA);
        public static readonly MqttPropertyType AUTHENTICATION_DATA =
                new MqttPropertyType(nameof(AUTHENTICATION_DATA), MqttProperties.AUTHENTICATION_DATA);

        private static readonly MqttPropertyType[] VALUES;

        static MqttPropertyType()
        {
            VALUES = new MqttPropertyType[43];

            foreach (MqttPropertyType v in Values())
            {
                VALUES[v.value] = v;
            }
        }

        private readonly int value;
        private readonly string name;

        private MqttPropertyType(string name, int value)
        {
            this.name = name;
            this.value = value;
        }

        public int Value()
        {
            return value;
        }

        public static MqttPropertyType ValueOf(int type)
        {
            MqttPropertyType t = null;

            try
            {
                t = VALUES[type];
            }
            catch (IndexOutOfRangeException)
            {
                // nop
            }

            if (t == null)
            {
                throw new ArgumentException("unknown property type: " + type.ToString(CultureInfo.InvariantCulture));
            }

            return t;
        }

        public string Name()
        {
            return name;
        }

        public static MqttPropertyType[] Values()
        {
            return new[]
            {
                PAYLOAD_FORMAT_INDICATOR, REQUEST_PROBLEM_INFORMATION, REQUEST_RESPONSE_INFORMATION, MAXIMUM_QOS,
                RETAIN_AVAILABLE, WILDCARD_SUBSCRIPTION_AVAILABLE, SUBSCRIPTION_IDENTIFIER_AVAILABLE,
                SHARED_SUBSCRIPTION_AVAILABLE, SERVER_KEEP_ALIVE, RECEIVE_MAXIMUM, TOPIC_ALIAS_MAXIMUM, TOPIC_ALIAS,
                PUBLICATION_EXPIRY_INTERVAL, SESSION_EXPIRY_INTERVAL, WILL_DELAY_INTERVAL, MAXIMUM_PACKET_SIZE,
                SUBSCRIPTION_IDENTIFIER, CONTENT_TYPE, RESPONSE_TOPIC, ASSIGNED_CLIENT_IDENTIFIER,
                AUTHENTICATION_METHOD, RESPONSE_INFORMATION, SERVER_REFERENCE, REASON_STRING, USER_PROPERTY,
                CORRELATION_DATA, AUTHENTICATION_DATA,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    public static readonly MqttProperties NO_PROPERTIES = new MqttProperties(false);

    internal static MqttProperties WithEmptyDefaults(MqttProperties properties)
    {
        if (properties == null)
        {
            return NO_PROPERTIES;
        }

        return properties;
    }

    // CLR: the source uses the raw type MqttProperty, "the name of a generic class or interface declaration without an
    // accompanying type argument list" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.8), for
    // properties of any value type. C# has no raw types; a generic class can inherit from a concrete class
    // (https://learn.microsoft.com/dotnet/csharp/programming-guide/generics/generic-classes), so this non-generic base
    // holds propertyId and the object-typed Value() that the raw type exposes, and MqttProperty<T> derives from it.
    public abstract class MqttProperty
    {
        internal readonly int propertyId;

        private protected MqttProperty(int propertyId)
        {
            this.propertyId = propertyId;
        }

        public object Value()
        {
            return BoxedValue();
        }

        /**
         * Get MQTT property ID
         * @return property ID
         */
        public int PropertyId()
        {
            return propertyId;
        }

        private protected abstract object BoxedValue();
    }

    /**
     * MQTT property base class
     *
     * @param <T> property type
     */
    public abstract class MqttProperty<T> : MqttProperty
    {
        internal readonly T value;

        protected MqttProperty(int propertyId, T value) : base(propertyId)
        {
            this.value = value;
        }

        /**
         * Get MQTT property value
         *
         * @return property value
         */
        public new T Value()
        {
            return value;
        }

        private protected override object BoxedValue()
        {
            return value;
        }

        public override int GetHashCode()
        {
            return propertyId + 31 * ValueHashCode(value);
        }

        public override bool Equals(object obj)
        {
            if (this == obj)
            {
                return true;
            }
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            MqttProperty<T> that = (MqttProperty<T>) obj;
            return this.propertyId == that.propertyId && ValueEquals(this.value, that.value);
        }

        // CLR: for a java.util.List value (UserProperties) value.equals/hashCode are List.equals, "the same elements in
        // the same order", and List.hashCode, 31*hashCode + element hash
        // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/List.html#equals(java.lang.Object));
        // a C# List<T> compares by reference, so list values are compared and hashed element by element here. Hash
        // values of other values (for example strings) differ from the JVM's; only the equals/hashCode contract is kept.
        private static bool ValueEquals(T value, T other)
        {
            if (value is IList list && other is IList otherList)
            {
                if (list.Count != otherList.Count)
                {
                    return false;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (!Equals(list[i], otherList[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return value.Equals(other);
        }

        private static int ValueHashCode(T value)
        {
            if (value is IList list)
            {
                int hashCode = 1;

                foreach (object e in list)
                {
                    hashCode = 31 * hashCode + (e == null ? 0 : e.GetHashCode());
                }

                return hashCode;
            }

            return value.GetHashCode();
        }
    }

    // CLR: Integer is int, so a property value cannot be null here; numbers in the text use the invariant culture
    // (https://learn.microsoft.com/dotnet/api/system.int32.tostring).
    public sealed class IntegerProperty : MqttProperty<int>
    {

        public IntegerProperty(int propertyId, int value) : base(propertyId, value)
        {
        }

        public override string ToString()
        {
            return "IntegerProperty(" + propertyId.ToString(CultureInfo.InvariantCulture) + ", " +
                    value.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    public sealed class StringProperty : MqttProperty<string>
    {

        public StringProperty(int propertyId, string value) : base(propertyId, value)
        {
        }

        // CLR: Java string conversion writes null as "null" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
        public override string ToString()
        {
            return "StringProperty(" + propertyId.ToString(CultureInfo.InvariantCulture) + ", " + (value ?? "null") + ")";
        }
    }

    public sealed class StringPair
    {
        public readonly string key;
        public readonly string value;

        public StringPair(string key, string value)
        {
            this.key = key;
            this.value = value;
        }

        public override int GetHashCode()
        {
            return key.GetHashCode() + 31 * value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (this == obj)
            {
                return true;
            }
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            StringPair that = (StringPair) obj;

            return that.key.Equals(this.key) && that.value.Equals(this.value);
        }
    }

    //User properties are the only properties that may be included multiple times and
    //are the only properties where ordering is required. Therefore, they need a special handling
    // CLR: java.util.List/ArrayList is List<T> and Collection is ICollection<T>.
    public sealed class UserProperties : MqttProperty<List<StringPair>>
    {
        public UserProperties() : base(USER_PROPERTY, new List<StringPair>())
        {
        }

        /**
         * Create user properties from the collection of the String pair values
         *
         * @param values string pairs. Collection entries are copied, collection itself isn't shared
         */
        public UserProperties(ICollection<StringPair> values) : this()
        {
            this.value.AddRange(values);
        }

        internal static UserProperties FromUserPropertyCollection(ICollection<UserProperty> properties)
        {
            UserProperties userProperties = new UserProperties();

            foreach (UserProperty property in properties)
            {
                userProperties.Add(new StringPair(property.value.key, property.value.value));
            }

            return userProperties;
        }

        public void Add(StringPair pair)
        {
            this.value.Add(pair);
        }

        public void Add(string key, string value)
        {
            this.value.Add(new StringPair(key, value));
        }

        // CLR: Java string conversion writes null as "null" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
        public override string ToString()
        {
            StringBuilder builder = new StringBuilder("UserProperties(");
            bool first = true;

            foreach (StringPair pair in value)
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append((pair.key ?? "null") + "->" + (pair.value ?? "null"));
                first = false;
            }

            builder.Append(")");

            return builder.ToString();
        }
    }

    public sealed class UserProperty : MqttProperty<StringPair>
    {
        public UserProperty(string key, string value) : base(USER_PROPERTY, new StringPair(key, value))
        {
        }

        // CLR: Java string conversion writes null as "null" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
        public override string ToString()
        {
            return "UserProperty(" + (value.key ?? "null") + ", " + (value.value ?? "null") + ")";
        }
    }

    public sealed class BinaryProperty : MqttProperty<byte[]>
    {

        public BinaryProperty(int propertyId, byte[] value) : base(propertyId, value)
        {
        }

        public override string ToString()
        {
            return "BinaryProperty(" + propertyId.ToString(CultureInfo.InvariantCulture) + ", " +
                    value.Length.ToString(CultureInfo.InvariantCulture) + " bytes)";
        }
    }

    public MqttProperties() : this(true)
    {
    }

    private MqttProperties(bool canModify)
    {
        this.canModify = canModify;
    }

    private Dictionary<int, MqttProperty> props;
    private List<UserProperty> userProperties;
    private List<IntegerProperty> subscriptionIds;
    private readonly bool canModify;

    public void Add(MqttProperty property)
    {
        if (!canModify)
        {
            throw new NotSupportedException("adding property isn't allowed");
        }

        Dictionary<int, MqttProperty> props = this.props;

        if (property.propertyId == USER_PROPERTY)
        {
            List<UserProperty> userProperties = this.userProperties;
            if (userProperties == null)
            {
                userProperties = new List<UserProperty>(1);
                this.userProperties = userProperties;
            }

            if (property is UserProperty)
            {
                userProperties.Add((UserProperty) property);
            }
            else if (property is UserProperties)
            {
                foreach (StringPair pair in ((UserProperties) property).value)
                {
                    userProperties.Add(new UserProperty(pair.key, pair.value));
                }
            }
            else
            {
                throw new ArgumentException("User property must be of UserProperty or UserProperties type");
            }
        }
        else if (property.propertyId == SUBSCRIPTION_IDENTIFIER)
        {
            List<IntegerProperty> subscriptionIds = this.subscriptionIds;
            if (subscriptionIds == null)
            {
                subscriptionIds = new List<IntegerProperty>(1);
                this.subscriptionIds = subscriptionIds;
            }

            if (property is IntegerProperty)
            {
                subscriptionIds.Add((IntegerProperty) property);
            }
            else
            {
                throw new ArgumentException("Subscription ID must be an integer property");
            }
        }
        else
        {
            if (props == null)
            {
                props = new Dictionary<int, MqttProperty>();
                this.props = props;
            }

            props[property.propertyId] = property;
        }
    }

    // CLR: Collection<? extends MqttProperty> accepts collections of any MqttProperty subtype; C# classes are invariant
    // and IReadOnlyCollection<out T> is covariant (https://learn.microsoft.com/dotnet/standard/generics/covariance-and-contravariance),
    // so it is the return type and a List<IntegerProperty> is returned without a copy, as in the source.
    public IReadOnlyCollection<MqttProperty> ListAll()
    {
        Dictionary<int, MqttProperty> props = this.props;
        if (props == null && subscriptionIds == null && userProperties == null)
        {
            return Array.Empty<MqttProperty>();
        }
        if (subscriptionIds == null && userProperties == null)
        {
            return props.Values;
        }
        if (props == null && userProperties == null)
        {
            return subscriptionIds;
        }

        List<MqttProperty> propValues = new List<MqttProperty>(props != null ? props.Count : 1);

        if (props != null)
        {
            propValues.AddRange(props.Values);
        }

        if (subscriptionIds != null)
        {
            propValues.AddRange(subscriptionIds);
        }

        if (userProperties != null)
        {
            propValues.Add(UserProperties.FromUserPropertyCollection(userProperties));
        }

        return propValues;
    }

    public bool IsEmpty()
    {
        Dictionary<int, MqttProperty> props = this.props;
        return (props == null || props.Count == 0) &&
                (userProperties == null || userProperties.Count == 0) &&
                (subscriptionIds == null || subscriptionIds.Count == 0);
    }

    /**
     * Get property by ID. If there are multiple properties of this type (can be with Subscription ID)
     * then return the first one.
     *
     * @param propertyId ID of the property
     * @return a property if it is set, null otherwise
     */
    public MqttProperty GetProperty(int propertyId)
    {
        if (propertyId == USER_PROPERTY)
        {
            //special handling to keep compatibility with earlier versions
            List<UserProperty> userProperties = this.userProperties;
            if (userProperties == null)
            {
                return null;
            }

            return UserProperties.FromUserPropertyCollection(userProperties);
        }

        if (propertyId == SUBSCRIPTION_IDENTIFIER)
        {
            List<IntegerProperty> subscriptionIds = this.subscriptionIds;
            if (subscriptionIds == null || subscriptionIds.Count == 0)
            {
                return null;
            }

            return subscriptionIds[0];
        }

        Dictionary<int, MqttProperty> props = this.props;
        return props == null ? null : props.TryGetValue(propertyId, out MqttProperty property) ? property : null;
    }

    /**
     * Get properties by ID.
     * Some properties (Subscription ID and User Properties) may occur multiple times,
     * this method returns all their values in order.
     *
     * @param propertyId ID of the property
     * @return all properties having specified ID
     */
    // CLR: List<? extends MqttProperty> is the covariant IReadOnlyList<out T> (see listAll()).
    public IReadOnlyList<MqttProperty> GetProperties(int propertyId)
    {
        if (propertyId == USER_PROPERTY)
        {
            return userProperties == null ? Array.Empty<MqttProperty>() : (IReadOnlyList<MqttProperty>) userProperties;
        }
        if (propertyId == SUBSCRIPTION_IDENTIFIER)
        {
            return subscriptionIds == null ? Array.Empty<MqttProperty>() : (IReadOnlyList<MqttProperty>) subscriptionIds;
        }

        Dictionary<int, MqttProperty> props = this.props;
        return (props == null || !props.ContainsKey(propertyId)) ?
                Array.Empty<MqttProperty>() :
                new[] { props[propertyId] };
    }
}
