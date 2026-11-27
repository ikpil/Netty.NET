/*
 * Copyright 2014 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * See <a href="https://public.dhe.ibm.com/software/dw/webservices/ws-mqtt/mqtt-v3r1.html#publish">MQTTV3.1/publish</a>
 */
// CLR: VariableHeader() and Payload() narrow the return types with covariant overrides
// (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override). ByteBufHolder.content() and
// refCnt() are the IByteBufHolder.Content and IReferenceCounted.RefCnt properties of the Buffer port. C# covariant
// returns apply to class overrides only and MqttMessage has no reference-counting members to override, so the methods
// that narrow ByteBufHolder/ReferenceCounted results return MqttPublishMessage and the IByteBufHolder members of the
// Buffer port and the IReferenceCounted members are implemented explicitly, as DatagramPacket does in Transport
// (https://learn.microsoft.com/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation).
public class MqttPublishMessage : MqttMessage, IByteBufHolder
{
    public MqttPublishMessage(
            MqttFixedHeader mqttFixedHeader,
            MqttPublishVariableHeader variableHeader,
            ByteBuf payload)
        : base(mqttFixedHeader, variableHeader, payload)
    {
    }

    public override MqttPublishVariableHeader VariableHeader()
    {
        return (MqttPublishVariableHeader) base.VariableHeader();
    }

    public override ByteBuf Payload()
    {
        return Content;
    }

    public virtual ByteBuf Content => (ByteBuf) base.Payload();

    public virtual MqttPublishMessage Copy()
    {
        return Replace(Content.Copy());
    }

    public virtual MqttPublishMessage Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public virtual MqttPublishMessage RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public virtual MqttPublishMessage Replace(ByteBuf content)
    {
        return new MqttPublishMessage(FixedHeader(), VariableHeader(), content);
    }

    public virtual int RefCnt => Content.RefCnt;

    public virtual MqttPublishMessage Retain()
    {
        Content.Retain();
        return this;
    }

    public virtual MqttPublishMessage Retain(int increment)
    {
        Content.Retain(increment);
        return this;
    }

    public virtual MqttPublishMessage Touch()
    {
        Content.Touch();
        return this;
    }

    public virtual MqttPublishMessage Touch(object hint)
    {
        Content.Touch(hint);
        return this;
    }

    public virtual bool Release()
    {
        return Content.Release();
    }

    public virtual bool Release(int decrement)
    {
        return Content.Release(decrement);
    }

    IByteBufHolder IByteBufHolder.Copy()
    {
        return Copy();
    }

    IByteBufHolder IByteBufHolder.Duplicate()
    {
        return Duplicate();
    }

    IByteBufHolder IByteBufHolder.RetainedDuplicate()
    {
        return RetainedDuplicate();
    }

    IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
    {
        return Replace(content);
    }

    IByteBufHolder IByteBufHolder.Retain()
    {
        return Retain();
    }

    IByteBufHolder IByteBufHolder.Retain(int increment)
    {
        return Retain(increment);
    }

    IByteBufHolder IByteBufHolder.Touch()
    {
        return Touch();
    }

    IByteBufHolder IByteBufHolder.Touch(object hint)
    {
        return Touch(hint);
    }

    IReferenceCounted IReferenceCounted.Retain()
    {
        return Retain();
    }

    IReferenceCounted IReferenceCounted.Retain(int increment)
    {
        return Retain(increment);
    }

    IReferenceCounted IReferenceCounted.Touch()
    {
        return Touch();
    }

    IReferenceCounted IReferenceCounted.Touch(object hint)
    {
        return Touch(hint);
    }

}
