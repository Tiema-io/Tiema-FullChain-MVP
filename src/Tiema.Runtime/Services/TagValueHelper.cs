using System;
using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Tiema.Tags.Grpc.V1; // updated namespace: tagsystem.proto generates types here

namespace Tiema.Runtime.Services
{
    // Helper: unpack protobuf TagValue / Update / Any into common CLR types (int/long/double/bool/string/byte[]).
    // - Returns true when a CLR value is unpacked into out value.
    // - On failure, value returns the original payload for higher-level handling.
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
                        // Update contains Tag or Batch
                        if (upd.PayloadCase == Update.PayloadOneofCase.Tag && upd.Tag != null)
                            return TryUnpackTagValue(upd.Tag, out value);
                        // For Batch, return original object without merging
                        value = upd.Batch != null ? (object)upd.Batch : (object)upd;
                        return false;

                    case Any any:
                        return TryUnpackAny(any, out value);

                    // Compatibility: transport may pass TagBatch directly
                    case TagBatch batch:
                        value = batch;
                        return false;

                    default:
                        // Unknown payload type: return false and keep raw payload
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
                    value = tv.BoolValue; return true;
                case TagValue.ValueOneofCase.IntValue:
                    // int64 defined for int_value in proto
                    value = tv.IntValue; return true;
                case TagValue.ValueOneofCase.DoubleValue:
                    value = tv.DoubleValue; return true;
                case TagValue.ValueOneofCase.StringValue:
                    value = tv.StringValue; return true;
                case TagValue.ValueOneofCase.BytesValue:
                    value = tv.BytesValue?.ToByteArray(); return true;
                case TagValue.ValueOneofCase.None:
                default:
                    value = tv; return false;
            }
        }

        private static bool TryUnpackAny(Any any, out object? value)
        {
            value = null;
            if (any == null) return false;

            try
            {
                if (any.Is(Int32Value.Descriptor))   { value = any.Unpack<Int32Value>().Value; return true; }
                if (any.Is(Int64Value.Descriptor))   { value = any.Unpack<Int64Value>().Value; return true; }
                if (any.Is(DoubleValue.Descriptor))  { value = any.Unpack<DoubleValue>().Value; return true; }
                if (any.Is(BoolValue.Descriptor))    { value = any.Unpack<BoolValue>().Value; return true; }
                if (any.Is(StringValue.Descriptor))  { value = any.Unpack<StringValue>().Value; return true; }
                if (any.Is(Struct.Descriptor))       { value = any.Unpack<Struct>(); return true; }
                if (any.Is(TagValue.Descriptor))     { var tv = any.Unpack<TagValue>(); return TryUnpackTagValue(tv, out value); }
                if (any.Is(TagBatch.Descriptor))     { value = any.Unpack<TagBatch>(); return false; }

                // unknown Any: keep raw
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