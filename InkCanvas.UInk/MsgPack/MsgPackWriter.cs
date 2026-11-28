using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InkUInk.MsgPack
{
    /// <summary>
    /// MessagePack 写入器。按 UInk TYPE-1 的要求，数值一律写成规范声明的精确类型与位宽，
    /// 不使用「最短编码」：uint16 固定 0xCD、uint32 固定 0xCE、float32 固定 0xCA 等。
    /// </summary>
    public sealed class MsgPackWriter
    {
        private readonly Stream _stream;
        private readonly byte[] _scratch = new byte[8];

        public MsgPackWriter(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            _stream = stream;
        }

        public void WriteNil()
        {
            _stream.WriteByte(0xC0);
        }

        public void WriteBool(bool value)
        {
            _stream.WriteByte(value ? (byte)0xC3 : (byte)0xC2);
        }

        public void WriteUInt16(ushort value)
        {
            _stream.WriteByte(0xCD);
            _scratch[0] = (byte)(value >> 8);
            _scratch[1] = (byte)value;
            _stream.Write(_scratch, 0, 2);
        }

        public void WriteUInt32(uint value)
        {
            _stream.WriteByte(0xCE);
            _scratch[0] = (byte)(value >> 24);
            _scratch[1] = (byte)(value >> 16);
            _scratch[2] = (byte)(value >> 8);
            _scratch[3] = (byte)value;
            _stream.Write(_scratch, 0, 4);
        }

        public void WriteUInt64(ulong value)
        {
            _stream.WriteByte(0xCF);
            for (int i = 0; i < 8; i++) _scratch[i] = (byte)(value >> (56 - i * 8));
            _stream.Write(_scratch, 0, 8);
        }

        public void WriteInt32(int value)
        {
            _stream.WriteByte(0xD2);
            uint u = unchecked((uint)value);
            _scratch[0] = (byte)(u >> 24);
            _scratch[1] = (byte)(u >> 16);
            _scratch[2] = (byte)(u >> 8);
            _scratch[3] = (byte)u;
            _stream.Write(_scratch, 0, 4);
        }

        public void WriteFloat32(float value)
        {
            _stream.WriteByte(0xCA);
            WriteBigEndian(BitConverter.GetBytes(value), 4);
        }

        public void WriteFloat64(double value)
        {
            _stream.WriteByte(0xCB);
            WriteBigEndian(BitConverter.GetBytes(value), 8);
        }

        private void WriteBigEndian(byte[] littleEndian, int length)
        {
            if (BitConverter.IsLittleEndian)
            {
                for (int i = length - 1; i >= 0; i--) _stream.WriteByte(littleEndian[i]);
            }
            else
            {
                _stream.Write(littleEndian, 0, length);
            }
        }

        /// <summary>str8 / str16 / str32，按 UTF-8 编码。</summary>
        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteNil();
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= 0xFF)
            {
                _stream.WriteByte(0xD9);
                _stream.WriteByte((byte)bytes.Length);
            }
            else if (bytes.Length <= 0xFFFF)
            {
                _stream.WriteByte(0xDA);
                _scratch[0] = (byte)(bytes.Length >> 8);
                _scratch[1] = (byte)bytes.Length;
                _stream.Write(_scratch, 0, 2);
            }
            else
            {
                _stream.WriteByte(0xDB);
                uint n = (uint)bytes.Length;
                _scratch[0] = (byte)(n >> 24);
                _scratch[1] = (byte)(n >> 16);
                _scratch[2] = (byte)(n >> 8);
                _scratch[3] = (byte)n;
                _stream.Write(_scratch, 0, 4);
            }

            _stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteArrayHeader(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count <= 15)
            {
                _stream.WriteByte((byte)(0x90 | count));
            }
            else if (count <= 0xFFFF)
            {
                _stream.WriteByte(0xDC);
                _scratch[0] = (byte)(count >> 8);
                _scratch[1] = (byte)count;
                _stream.Write(_scratch, 0, 2);
            }
            else
            {
                _stream.WriteByte(0xDD);
                uint n = (uint)count;
                _scratch[0] = (byte)(n >> 24);
                _scratch[1] = (byte)(n >> 16);
                _scratch[2] = (byte)(n >> 8);
                _scratch[3] = (byte)n;
                _stream.Write(_scratch, 0, 4);
            }
        }

        public void WriteMapHeader(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count <= 15)
            {
                _stream.WriteByte((byte)(0x80 | count));
            }
            else if (count <= 0xFFFF)
            {
                _stream.WriteByte(0xDE);
                _scratch[0] = (byte)(count >> 8);
                _scratch[1] = (byte)count;
                _stream.Write(_scratch, 0, 2);
            }
            else
            {
                _stream.WriteByte(0xDF);
                uint n = (uint)count;
                _scratch[0] = (byte)(n >> 24);
                _scratch[1] = (byte)(n >> 16);
                _scratch[2] = (byte)(n >> 8);
                _scratch[3] = (byte)n;
                _stream.Write(_scratch, 0, 4);
            }
        }

        /// <summary>写 Map 的一个字符串键。</summary>
        public void WriteKey(string key)
        {
            WriteString(key);
        }
    }
}
