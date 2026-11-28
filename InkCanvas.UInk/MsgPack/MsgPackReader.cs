using System;
using System.Collections.Generic;
using System.Text;

namespace InkUInk.MsgPack
{
    /// <summary>文件在最后一个对象中间结束（FILE-16）：丢弃该对象，保留之前的对象。</summary>
    public sealed class MsgPackTruncatedException : Exception
    {
        public MsgPackTruncatedException(string message) : base(message) { }
    }

    /// <summary>无法解码的字节（FILE-17）：停止读取，不向后搜索。</summary>
    public sealed class MsgPackCorruptException : Exception
    {
        public MsgPackCorruptException(string message) : base(message) { }
    }

    /// <summary>同一个 Map 重复出现已知键（TYPE-7）：包含该 Map 的整个块无效。</summary>
    public sealed class MsgPackDuplicateKeyException : Exception
    {
        internal MsgPackDuplicateKeyException(object value, List<object> duplicateRootTypes)
            : base("Map 中重复出现键")
        {
            Value = value;
            DuplicateRootTypes = duplicateRootTypes;
        }

        internal object Value { get; }
        internal IReadOnlyList<object> DuplicateRootTypes { get; }
    }

    /// <summary>
    /// MessagePack 读取器，把对象解码为 object 树：
    /// 整数 → long（超出 long 范围的 uint64 → ulong）、浮点 → double、str → string、
    /// bin → byte[]、array → List&lt;object&gt;、map → Dictionary&lt;string, object&gt;。
    /// 未知键由调用方忽略（TYPE-8），因此不需要预先知道结构。
    /// </summary>
    public sealed class MsgPackReader
    {
        private const int MaxDepth = 64;

        private readonly byte[] _buffer;
        private readonly int _end;
        private int _position;
        private bool _hasDuplicateKey;
        private List<object> _duplicateRootTypes;

        public MsgPackReader(byte[] buffer) : this(buffer, 0, buffer == null ? 0 : buffer.Length) { }

        public MsgPackReader(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            _buffer = buffer;
            _position = offset;
            _end = offset + count;
        }

        public int Position
        {
            get { return _position; }
        }

        public bool EndOfStream
        {
            get { return _position >= _end; }
        }

        /// <summary>解码一个完整对象。截断抛 MsgPackTruncatedException，非法字节抛 MsgPackCorruptException。</summary>
        public object ReadValue()
        {
            _hasDuplicateKey = false;
            _duplicateRootTypes = null;
            object value = ReadValue(0);
            if (_hasDuplicateKey) throw new MsgPackDuplicateKeyException(value, _duplicateRootTypes);
            return value;
        }

        private object ReadValue(int depth)
        {
            if (depth > MaxDepth) throw new MsgPackCorruptException("嵌套过深");
            if (_position >= _end) throw new MsgPackTruncatedException("对象不完整");

            byte code = _buffer[_position++];

            if (code <= 0x7F) return (long)code;                       // positive fixint
            if (code >= 0xE0) return (long)(sbyte)code;                // negative fixint

            if (code >= 0x80 && code <= 0x8F) return ReadMap(code & 0x0F, depth);
            if (code >= 0x90 && code <= 0x9F) return ReadArray(code & 0x0F, depth);
            if (code >= 0xA0 && code <= 0xBF) return ReadString(code & 0x1F);

            switch (code)
            {
                case 0xC0: return null;
                case 0xC1: throw new MsgPackCorruptException("0xC1 是 MessagePack 保留的无效字节");
                case 0xC2: return false;
                case 0xC3: return true;
                case 0xC4: return ReadBinary(ReadUInt8());
                case 0xC5: return ReadBinary(ReadUInt16());
                case 0xC6: return ReadBinary(checked((int)ReadUInt32()));
                case 0xCA: return (double)ReadSingle();
                case 0xCB: return ReadDouble();
                case 0xCC: return (long)ReadUInt8();
                case 0xCD: return (long)ReadUInt16();
                case 0xCE: return (long)ReadUInt32();
                case 0xCF:
                {
                    ulong v = ReadUInt64();
                    return v <= long.MaxValue ? (object)(long)v : v;
                }
                case 0xD0: return (long)(sbyte)ReadUInt8();
                case 0xD1: return (long)(short)ReadUInt16();
                case 0xD2: return (long)(int)ReadUInt32();
                case 0xD3: return ReadInt64();
                case 0xD9: return ReadString(ReadUInt8());
                case 0xDA: return ReadString(ReadUInt16());
                case 0xDB: return ReadString(checked((int)ReadUInt32()));
                case 0xDC: return ReadArray(ReadUInt16(), depth);
                case 0xDD: return ReadArray(checked((int)ReadUInt32()), depth);
                case 0xDE: return ReadMap(ReadUInt16(), depth);
                case 0xDF: return ReadMap(checked((int)ReadUInt32()), depth);
                default:
                    // fixext / ext 系列（1.1 未使用）
                    throw new MsgPackCorruptException($"不支持的 MessagePack 类型字节 0x{code:X2}");
            }
        }

        private List<object> ReadArray(int count, int depth)
        {
            if (count < 0) throw new MsgPackCorruptException("数组长度无效");
            var list = new List<object>(Math.Min(count, 1024));
            for (int i = 0; i < count; i++) list.Add(ReadValue(depth + 1));
            return list;
        }

        private Dictionary<string, object> ReadMap(int count, int depth)
        {
            if (count < 0) throw new MsgPackCorruptException("Map 长度无效");
            var map = new Dictionary<string, object>(StringComparer.Ordinal);

            for (int i = 0; i < count; i++)
            {
                object keyValue = ReadValue(depth + 1);
                string key = keyValue as string;
                if (key == null) throw new MsgPackCorruptException("Map 的键不是字符串");

                object value = ReadValue(depth + 1);
                if (map.ContainsKey(key))
                {
                    _hasDuplicateKey = true;
                    if (depth == 0 && key == "type")
                    {
                        if (_duplicateRootTypes == null) _duplicateRootTypes = new List<object>();
                        _duplicateRootTypes.Add(value);
                    }
                }
                else map[key] = value;
            }

            return map;
        }

        private byte ReadByte()
        {
            if (_position >= _end) throw new MsgPackTruncatedException("字节不足");
            return _buffer[_position++];
        }

        private byte[] ReadRaw(int count)
        {
            if (count < 0) throw new MsgPackCorruptException("长度无效");
            if (count > _end - _position) throw new MsgPackTruncatedException("字节不足");
            byte[] result = new byte[count];
            Buffer.BlockCopy(_buffer, _position, result, 0, count);
            _position += count;
            return result;
        }

        private byte ReadUInt8() { return ReadByte(); }

        private ushort ReadUInt16()
        {
            var b = ReadRaw(2);
            return (ushort)((b[0] << 8) | b[1]);
        }

        private uint ReadUInt32()
        {
            var b = ReadRaw(4);
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private ulong ReadUInt64()
        {
            var b = ReadRaw(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[i];
            return v;
        }

        private long ReadInt64()
        {
            return unchecked((long)ReadUInt64());
        }

        private float ReadSingle()
        {
            var b = ReadRaw(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }

        private double ReadDouble()
        {
            var b = ReadRaw(8);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToDouble(b, 0);
        }

        private string ReadString(int count)
        {
            var bytes = ReadRaw(count);
            return Encoding.UTF8.GetString(bytes);
        }

        private byte[] ReadBinary(int count)
        {
            return ReadRaw(count);
        }
    }
}
