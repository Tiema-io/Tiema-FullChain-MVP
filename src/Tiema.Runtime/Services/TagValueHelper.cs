using System;
using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Tiema.Protocols.V1;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// Helper: 解包 protobuf TagValue / Update / Any 到常见 CLR 类型（int/long/double/bool/string/byte[]）。
    /// - 返回 true 表示成功解包出 CLR 值到 out value。
    /// - 失败时 value 返回原始 payload（供上层做特殊处理）。
    /// </summary>
    public static class TagValueHelper
    {
        public static bool TryUnpack(object? payload, out object? value)
        {
            value = null;
            if (payload == null) return false;

            try
            {
                switch (payload)
                {
                    case TagValue tv:
                        return TryUnpackTagValue(tv, out value);

                    case Update upd:
                        // Update 包含 Tag 或 Batch
                        if (upd.PayloadCase == Update.PayloadOneofCase.Tag && upd.Tag != null)
                            return TryUnpackTagValue(upd.Tag, out value);
                        // 对于 Batch 不做合并解包，直接返回 batch 原始对象
                        value = upd.Batch != null ? (object)upd.Batch : (object)upd;
                        return false;

                    case Any any:
                        return TryUnpackAny(any, out value);

                    // 兼容：有时 transport 可能直接传 TagBatch
                    case TagBatch batch:
                        value = batch;
                        return false;

                    default:
                        // 不是我们认识的类型，返回 false 并保留原始 payload
                        value = payload;
                        return false;
                }
            }
            catch
            {
                value = payload;
                return false;
            }
        }

        private static bool TryUnpackTagValue(TagValue tv, out object? value)
        {
            value = null;
            if (tv == null) return false;

            switch (tv.ValueCase)
            {
                case TagValue.ValueOneofCase.BoolValue:
                    value = tv.BoolValue;
                    return true;
                case TagValue.ValueOneofCase.IntValue:
                    // proto 定义 int64 用于 int_value 字段
                    value = tv.IntValue;
                    return true;
                case TagValue.ValueOneofCase.DoubleValue:
                    value = tv.DoubleValue;
                    return true;
                case TagValue.ValueOneofCase.StringValue:
                    value = tv.StringValue;
                    return true;
                case TagValue.ValueOneofCase.BytesValue:
                    value = tv.BytesValue?.ToByteArray();
                    return true;
                case TagValue.ValueOneofCase.None:
                default:
                    // 如果没有 oneof 值，尝试看是否把 TagValue 本身作为 wrapper 的 Any 存在（不在此处理）
                    value = tv;
                    return false;
            }
        }

        private static bool TryUnpackAny(Any any, out object? value)
        {
            value = null;
            if (any == null) return false;

            try
            {
                if (any.Is(Int32Value.Descriptor))
                {
                    var v = any.Unpack<Int32Value>();
                    value = v.Value;
                    return true;
                }
                if (any.Is(Int64Value.Descriptor))
                {
                    var v = any.Unpack<Int64Value>();
                    value = v.Value;
                    return true;
                }
                if (any.Is(DoubleValue.Descriptor))
                {
                    var v = any.Unpack<DoubleValue>();
                    value = v.Value;
                    return true;
                }
                if (any.Is(BoolValue.Descriptor))
                {
                    var v = any.Unpack<BoolValue>();
                    value = v.Value;
                    return true;
                }
                if (any.Is(StringValue.Descriptor))
                {
                    var v = any.Unpack<StringValue>();
                    value = v.Value;
                    return true;
                }
                if (any.Is(Struct.Descriptor))
                {
                    var v = any.Unpack<Struct>();
                    value = v;
                    return true;
                }
                if (any.Is(TagValue.Descriptor))
                {
                    var tv = any.Unpack<TagValue>();
                    return TryUnpackTagValue(tv, out value);
                }
                if (any.Is(TagBatch.Descriptor))
                {
                    value = any.Unpack<TagBatch>();
                    return false;
                }

                // unknown any: keep raw
                value = any;
                return false;
            }
            catch
            {
                value = any;
                return false;
            }
        }
    }
}