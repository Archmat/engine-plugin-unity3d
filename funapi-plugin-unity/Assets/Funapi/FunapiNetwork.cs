// Copyright (C) 2013-2014 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Protobuf
using funapi.network.fun_message;


namespace Fun
{
    // Funapi version
    public class FunapiVersion
    {
        public static readonly int kProtocolVersion = 1;
        public static readonly int kPluginVersion = 63;
    }

    // Funapi transport protocol
    public enum TransportProtocol {
        kDefault = 0,
        kTcp,
        kUdp,
        kHttp
    };

    // Funapi message type
    public enum FunMsgType
    {
        kJson,
        kProtobuf
    }

    // Error code
    public enum ErrorCode
    {
        kNone,
        kConnectFailed,
        kSendFailed,
        kReceiveFailed,
        kEncryptionFailed,
        kInvalidEncryption,
        kUnknownEncryption,
        kExceptionError
    }

    // Event handler delegate
    public delegate void ReceivedEventHandler(TransportProtocol protocol,
                                              Dictionary<string, string> header, ArraySegment<byte> body);
    public delegate void ConnectTimeoutHandler(TransportProtocol protocol);
    public delegate void StartedEventHandler(TransportProtocol protocol);
    public delegate void StoppedEventHandler(TransportProtocol protocol);

    // Container to hold json-related functions.
    public abstract class JsonAccessor
    {
        public abstract string Serialize(object json_obj);
        public abstract object Deserialize(string json_str);
        public abstract string GetStringField(object json_obj, string field_name);
        public abstract void SetStringField(object json_obj, string field_name, string value);
        public abstract Int64 GetIntegerField(object json_obj, string field_name);
        public abstract void SetIntegerField(object json_obj, string field_name, Int64 value);
        public abstract bool HasField(object json_obj, string field_name);
        public abstract void RemoveStringField(object json_obj, string field_name);
        public abstract object Clone(object json_obj);
    }

    public class DictionaryJsonAccessor : JsonAccessor
    {
        public override string Serialize(object json_obj)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return Json.Serialize(d);
        }

        public override object Deserialize(string json_string)
        {
            return Json.Deserialize(json_string) as Dictionary<string, object>;
        }

        public override string GetStringField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return d[field_name] as string;
        }

        public override void SetStringField(object json_obj, string field_name, string value)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            d[field_name] = value;
        }

        public override Int64 GetIntegerField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return Convert.ToInt64(d [field_name]);
        }

        public override void SetIntegerField(object json_obj, string field_name, Int64 value)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert (d != null);
            d [field_name] = value;
        }

        public override bool HasField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert (d != null);
            return d.ContainsKey (field_name);
        }

        public override void RemoveStringField(object json_obj, string field_name)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            d.Remove(field_name);
        }

        public override object Clone(object json_obj)
        {
            Dictionary<string, object> d = json_obj as Dictionary<string, object>;
            DebugUtils.Assert(d != null);
            return new Dictionary<string, object>(d);

        }
    }


    // Abstract class to represent Transport used by Funapi
    // There are 3 transport types at the moment (though this plugin implements only TCP one.)
    // TCP, UDP, and HTTP.
    public abstract class FunapiTransport
    {
        #region public interface
        // Start connecting
        public abstract void Start();

        // Disconnection
        public abstract void Stop();

        // Check connection
        public abstract bool Started { get; }

        // Update
        public virtual void Update () {}

        // Check unsent messages
        public abstract bool HaveUnsentMessages { get; }

        // Send a message
        public abstract void SendMessage(string msgtype, object json_message);
        public abstract void SendMessage(FunMessage message);

        // Registered event handlers.
        public event ConnectTimeoutHandler ConnectTimeoutCallback;
        public event StartedEventHandler StartedCallback;
        public event StoppedEventHandler StoppedCallback;
        public event ReceivedEventHandler ReceivedCallback;

        // Transport protocol
        public TransportProtocol protocol
        {
            get { return protocol_; }
        }

        public State state
        {
            get { return state_; }
        }

        internal void SetState (State s)
        {
            state_ = s;
        }

        // Encoding/Decoding related
        public JsonAccessor JsonHelper
        {
            get { return json_accessor_; }
            set { json_accessor_ = value; }
        }

        // FunMessage serializer/deserializer
        public FunMessageSerializer ProtobufHelper {
            get { return serializer_; }
            set { serializer_ = value; }
        }
        #endregion

        #region internal implementation
        protected void OnConnectionTimeout ()
        {
            ConnectTimeoutCallback(protocol_);
        }

        protected void OnReceived (Dictionary<string, string> header, ArraySegment<byte> body)
        {
            ReceivedCallback(protocol_, header, body);
        }

        protected void OnStarted ()
        {
            if (StartedCallback != null)
            {
                StartedCallback(protocol_);
            }
        }

        protected void OnStopped ()
        {
            StoppedCallback(protocol_);
        }

        public virtual bool IsStream
        {
            get { return false; }
        }

        public virtual bool IsDatagram
        {
            get { return false; }
        }

        public virtual bool IsRequestResponse
        {
            get { return false; }
        }

        public float ConnectTimeout
        {
            get; set;
        }

        public ErrorCode LastErrorCode
        {
            get { return last_error_code_; }
        }

        public string LastErrorMessage
        {
            get { return last_error_message_; }
        }


        public enum State
        {
            kUnknown = 0,
            kConnecting,
            kConnected
        };



        // member variables.
        protected State state_ = State.kUnknown;
        protected TransportProtocol protocol_ = TransportProtocol.kDefault;
        protected JsonAccessor json_accessor_ = new DictionaryJsonAccessor();
        protected FunMessageSerializer serializer_ = null;
        protected ErrorCode last_error_code_ = ErrorCode.kNone;
        protected string last_error_message_ = "";
        #endregion
    }


    // Transport class for socket
    public abstract class FunapiDecodedTransport : FunapiTransport
    {
        // Create a socket.
        protected abstract void Init();

        // Sends a packet.
        protected abstract void WireSend();

        #region public interface
        // Starts a socket.
        public override void Start()
        {
            bool failed = false;

            try
            {
                // Resets states.
                header_decoded_ = false;
                received_size_ = 0;
                next_decoding_offset_ = 0;
                header_fields_.Clear();
                sending_.Clear();
                last_error_code_ = ErrorCode.kNone;
                last_error_message_ = "";

                Init();
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in Start: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        // Stops a socket.
        public override void Stop()
        {
            if (state_ == State.kUnknown)
                return;

            state_ = State.kUnknown;
            last_error_code_ = ErrorCode.kNone;
            last_error_message_ = "";

            OnStopped();
        }

        public override bool HaveUnsentMessages
        {
            get
            {
                lock (sending_lock_)
                {
                    return sending_.Count > 0 || pending_.Count > 0;
                }
            }
        }

        // Sends a JSON message through a socket.
        public override void SendMessage (string msgtype, object json_message)
        {
            string str = this.JsonHelper.Serialize(json_message);
            byte[] body = Encoding.UTF8.GetBytes(str);

            DebugUtils.Log("JSON to send : " + str);

            SendMessage(msgtype, body);
        }

        public override void SendMessage (FunMessage message)
        {
            MemoryStream stream = new MemoryStream();
            this.ProtobufHelper.Serialize (stream, message);

            byte[] body = new byte[stream.Length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(body, 0, body.Length);

            SendMessage(message.msgtype, body);
        }
        #endregion

        #region internal implementation
        private void SendMessage (string msgtype, byte[] body)
        {
            bool failed = false;

            try
            {
                lock (sending_lock_)
                {
                    string header = "";
                    header += kVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kProtocolVersion + kHeaderDelimeter;
                	if (first_sending_)
                    {
                        header += kPluginVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kPluginVersion + kHeaderDelimeter;
                    	first_sending_ = false;
                    }
                    header += kLengthHeaderField + kHeaderFieldDelimeter + body.Length + kHeaderDelimeter;
                    header += kHeaderDelimeter;

                    pending_.Add(new SendingBuffer(msgtype, new ArraySegment<byte>(Encoding.ASCII.GetBytes(header))));
                    pending_.Add(new SendingBuffer(msgtype, new ArraySegment<byte>(body)));

                    if (Started && sending_.Count == 0)
                    {
                        List<SendingBuffer> tmp = sending_;
                        sending_ = pending_;
                        pending_ = tmp;

                    	WireSend();
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendMessage: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        protected bool SendUnsentMessages ()
        {
            lock (sending_lock_)
            {
                if (sending_.Count > 0)
                {
                    // If we have more segments to send, we process more.
                    Debug.Log("Retrying unsent messages.");
                    WireSend();
                }
                else if (pending_.Count > 0)
                {
                    // Otherwise, try to process pending messages.
                    List<SendingBuffer> tmp = sending_;
                    sending_ = pending_;
                    pending_ = tmp;

                    WireSend();
                }
            }

            return true;
        }

        // Checks buffer space before starting another async receive.
        protected void CheckReceiveBuffer()
        {
            int remaining_size = receive_buffer_.Length - received_size_;

            if (remaining_size <= 0)
            {
                byte[] new_buffer = null;

                if (remaining_size == 0 && next_decoding_offset_ > 0)
                    new_buffer = new byte[receive_buffer_.Length];
                else
                    new_buffer = new byte[receive_buffer_.Length + kUnitBufferSize];

                // If there are space can be collected, compact it first.
                // Otherwise, increase the receiving buffer size.
                if (next_decoding_offset_ > 0)
                {
                    DebugUtils.Log("Compacting a receive buffer to save " + next_decoding_offset_ + " bytes.");
                    Buffer.BlockCopy(receive_buffer_, next_decoding_offset_, new_buffer, 0, received_size_ - next_decoding_offset_);
                    receive_buffer_ = new_buffer;
                    received_size_ -= next_decoding_offset_;
                    next_decoding_offset_ = 0;
                }
                else
                {
                    DebugUtils.Log("Increasing a receive buffer to " + (receive_buffer_.Length + kUnitBufferSize) + " bytes.");
                    Buffer.BlockCopy(receive_buffer_, 0, new_buffer, 0, received_size_);
                    receive_buffer_ = new_buffer;
                }
            }
        }

        protected bool TryToDecodeHeader()
        {
            DebugUtils.Log("Trying to decode header fields.");

            for (; next_decoding_offset_ < received_size_; )
            {
                ArraySegment<byte> haystack = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, received_size_ - next_decoding_offset_);
                int offset = BytePatternMatch(haystack, kHeaderDelimeterAsNeedle);
                if (offset < 0)
                {
                    // Not enough bytes. Wait for more bytes to come.
                    DebugUtils.Log("We need more bytes for a header field. Waiting.");
                    return false;
                }
                string line = Encoding.ASCII.GetString(receive_buffer_, next_decoding_offset_, offset - next_decoding_offset_);
                next_decoding_offset_ = offset + 1;

                if (line == "")
                {
                    // End of header.
                    header_decoded_ = true;
                    DebugUtils.Log("End of header reached. Will decode body from now.");
                    return true;
                }

                DebugUtils.Log("Header line: " + line);
                string[] tuple = line.Split(kHeaderFieldDelimeterAsChars);
                tuple[0] = tuple[0].ToUpper();
                DebugUtils.Log("Decoded header field '" + tuple[0] + "' => '" + tuple[1] + "'");
                DebugUtils.Assert(tuple.Length == 2);
                header_fields_[tuple[0]] = tuple[1];
            }

            return false;
        }

        protected bool TryToDecodeBody()
        {
            // Header version
            DebugUtils.Assert(header_fields_.ContainsKey(kVersionHeaderField));
            int version = Convert.ToUInt16(header_fields_[kVersionHeaderField]);
            DebugUtils.Assert(version == FunapiVersion.kProtocolVersion);

            // Header length
            DebugUtils.Assert(header_fields_.ContainsKey(kLengthHeaderField));
            int body_length = Convert.ToInt32(header_fields_[kLengthHeaderField]);
            DebugUtils.Log("We need " + body_length + " bytes for a message body. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");

            if (received_size_ - next_decoding_offset_ < body_length)
            {
                // Need more bytes.
                DebugUtils.Log("We need more bytes for a message body. Waiting.");
                return false;
            }

            if (body_length > 0)
            {
                DebugUtils.Assert(state_ == State.kConnected);

                if (state_ != State.kConnected)
                {
                    DebugUtils.Log("Unexpected message.");
                    return false;
                }

                ArraySegment<byte> body = new ArraySegment<byte>(receive_buffer_, next_decoding_offset_, body_length);
                next_decoding_offset_ += body_length;

                // The network module eats the fields and invoke registered handler.
                OnReceived(header_fields_, body);
            }

            // Prepares a next message.
            header_decoded_ = false;
            header_fields_.Clear();
            return true;
        }

        private static int BytePatternMatch (ArraySegment<byte> haystack, ArraySegment<byte> needle)
        {
            if (haystack.Count < needle.Count)
            {
                return -1;
            }

            for (int i = 0; i <= haystack.Count - needle.Count; ++i)
            {
                bool found = true;
                for (int j = 0; j < needle.Count; ++j)
                {
                    if (haystack.Array[haystack.Offset + i + j] != needle.Array[needle.Offset + j])
                    {
                        found = false;
                    }
                }
                if (found)
                {
                    return haystack.Offset + i;
                }
            }

            return -1;
        }
        #endregion


        protected class SendingBuffer
        {
            public SendingBuffer (string msgtype, ArraySegment<byte> data)
            {
                this.msgtype = msgtype;
                this.data = data;
            }

            public string msgtype;
            public ArraySegment<byte> data;
        }


        // Buffer-related constants.
        protected static readonly int kUnitBufferSize = 65536;

        // Funapi header-related constants.
        protected static readonly string kHeaderDelimeter = "\n";
    	protected static readonly string kHeaderFieldDelimeter = ":";
    	protected static readonly string kVersionHeaderField = "VER";
    	protected static readonly string kPluginVersionHeaderField = "PVER";
    	protected static readonly string kLengthHeaderField = "LEN";

        // for speed-up.
        private static readonly ArraySegment<byte> kHeaderDelimeterAsNeedle = new ArraySegment<byte>(Encoding.ASCII.GetBytes(kHeaderDelimeter));
        private static readonly char[] kHeaderFieldDelimeterAsChars = kHeaderFieldDelimeter.ToCharArray();

        // State-related.
        private bool first_sending_ = true;
        protected bool header_decoded_ = false;
        protected int received_size_ = 0;
        protected int next_decoding_offset_ = 0;
        protected object sending_lock_ = new object();
        protected object receive_lock_ = new object();
        protected byte[] receive_buffer_ = new byte[kUnitBufferSize];
        protected byte[] send_buffer_ = new byte[kUnitBufferSize];
        protected List<SendingBuffer> pending_ = new List<SendingBuffer>();
        protected List<SendingBuffer> sending_ = new List<SendingBuffer>();
        protected Dictionary<string, string> header_fields_ = new Dictionary<string, string>();
    }


    // TCP transport layer
    public class FunapiTcpTransport : FunapiDecodedTransport
    {
        #region public interface
        public FunapiTcpTransport (string hostname_or_ip, UInt16 port)
        {
            protocol_ = TransportProtocol.kTcp;
            DisableNagle = false;

            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            connect_ep_ = new IPEndPoint(address, port);
        }

        // Stops a socket.
        public override void Stop()
        {
            if (state_ == State.kUnknown)
                return;

            if (sock_ != null)
            {
                sock_.Close();
                sock_ = null;
            }

            base.Stop();
        }

        public override bool Started
        {
            get
            {
                return sock_ != null && sock_.Connected && state_ == State.kConnected;
            }
        }

        public override bool IsStream
        {
            get { return true; }
        }

        public bool DisableNagle
        {
            get; set;
        }

        public override void Update ()
        {
            if (state_ == State.kConnecting && connect_timeout_ > 0f)
            {
                connect_timeout_ -= Time.deltaTime;
                if (connect_timeout_ <= 0f)
                {
                    DebugUtils.Log("Connection waiting time has been exceeded.");
                    OnConnectionTimeout();
                }
            }
        }
        #endregion

        #region internal implementation
        // Create a socket.
        protected override void Init()
        {
            state_ = State.kConnecting;
            connect_timeout_ = ConnectTimeout;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            if (DisableNagle)
                sock_.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

            sock_.BeginConnect(connect_ep_, new AsyncCallback(this.StartCb), this);
        }

        protected override void WireSend()
        {
            List<ArraySegment<byte>> list = new List<ArraySegment<byte>>();
            lock (sending_lock_)
            {
                foreach (SendingBuffer buffer in sending_)
                {
                    list.Add(buffer.data);
                }
            }

            sock_.BeginSend(list, 0, new AsyncCallback(this.SendBytesCb), this);
        }

        private void StartCb(IAsyncResult ar)
        {
            DebugUtils.Log("StartCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                sock_.EndConnect(ar);
                if (sock_.Connected == false)
                {
                    last_error_code_ = ErrorCode.kConnectFailed;
                    last_error_message_ = "Failed to connect.";
                    DebugUtils.Log(last_error_message_);
                    return;
                }
                Debug.Log("Connected.");

                state_ = State.kConnected;

                OnStarted();

                lock (receive_lock_)
                {
                    // Wait for encryption handshaking message.
                    ArraySegment<byte> wrapped = new ArraySegment<byte>(receive_buffer_, 0, receive_buffer_.Length);
                    List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                    buffer.Add(wrapped);
                    sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginConnect operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in StartCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("SendBytesCb called.");

            bool failed = false;

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                int nSent = sock_.EndSend(ar);
                DebugUtils.Log("Sent " + nSent + "bytes");

                lock (sending_lock_)
                {
                    // Removes any segment fully sent.
                    while (nSent > 0)
                    {
                        DebugUtils.Assert(sending_.Count > 0);

                        if (sending_[0].data.Count > nSent)
                        {
                            // partial data
                            DebugUtils.Log("Partially sent. Will resume.");
                            break;
                        }
                        else
                        {
                            // fully sent.
                            DebugUtils.Log("Discarding a fully sent message.");
                            nSent -= sending_[0].data.Count;
                            sending_.RemoveAt(0);
                        }
                    }

                    while (sending_.Count > 0 && sending_[0].data.Count <= 0)
                    {
                        DebugUtils.Log("Remove zero byte buffer.");
                        sending_.RemoveAt(0);
                    }

                    // If the first segment has been sent partially, we need to reconstruct the first segment.
                    if (nSent > 0)
                    {
                        DebugUtils.Assert(sending_.Count > 0);
                        ArraySegment<byte> original = sending_[0].data;

                        DebugUtils.Assert(nSent <= sending_[0].data.Count);
                        ArraySegment<byte> adjusted = new ArraySegment<byte>(original.Array, original.Offset + nSent, original.Count - nSent);
                        sending_[0].data = adjusted;

                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }

                    if (!SendUnsentMessages())
                        failed = true;
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginSend operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        DebugUtils.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
                    }

                    // Try to decode as many messages as possible.
                    while (true)
                    {
                        if (header_decoded_ == false)
                        {
                            if (TryToDecodeHeader() == false)
                            {
                                break;
                            }
                        }
                        if (header_decoded_)
                        {
                            if (TryToDecodeBody() == false)
                            {
                                break;
                            }
                        }
                    }

                    if (nRead > 0)
                    {
                        // Checks buffer space
                        CheckReceiveBuffer();

                        // Starts another async receive
                        ArraySegment<byte> residual = new ArraySegment<byte>(receive_buffer_, received_size_, receive_buffer_.Length - received_size_);
                        List<ArraySegment<byte>> buffer = new List<ArraySegment<byte>>();
                        buffer.Add(residual);
                        sock_.BeginReceive(buffer, 0, new AsyncCallback(this.ReceiveBytesCb), this);
                        DebugUtils.Log("Ready to receive more. We can receive upto " + (receive_buffer_.Length - received_size_) + " more bytes");
                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        DebugUtils.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            DebugUtils.Log("Buffer has " + (receive_buffer_.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                        }
                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can't not receive messages. Maybe the socket is closed.";
                        DebugUtils.Log(last_error_message_);
                        failed = true;
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginReceive operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        protected Socket sock_;
        private IPEndPoint connect_ep_;
        private float connect_timeout_ = 0f;
        #endregion
    }


    // UDP transport layer
    public class FunapiUdpTransport : FunapiDecodedTransport
    {
        #region public interface
        public FunapiUdpTransport(string hostname_or_ip, UInt16 port)
        {
            protocol_ = TransportProtocol.kUdp;
            IPHostEntry host_info = Dns.GetHostEntry(hostname_or_ip);
            DebugUtils.Assert(host_info.AddressList.Length == 1);
            IPAddress address = host_info.AddressList[0];
            send_ep_ = new IPEndPoint(address, port);
            receive_ep_ = (EndPoint)new IPEndPoint(IPAddress.Any, port);
        }

        // Stops a socket.
        public override void Stop()
        {
            if (state_ == State.kUnknown)
                return;

            if (sock_ != null)
            {
                sock_.Close();
                sock_ = null;
            }

            base.Stop();
        }

        public override bool Started
        {
            get { return sock_ != null && state_ == State.kConnected; }
        }

        public override bool IsDatagram
        {
            get { return true; }
        }
        #endregion

        #region internal implementation
        // Create a socket.
        protected override void Init()
        {
            state_ = State.kConnected;
            sock_ = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock_.BeginReceiveFrom(receive_buffer_, 0, receive_buffer_.Length, SocketFlags.None,
                                   ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

            OnStarted();
        }

        // Send a packet.
        protected override void WireSend()
        {
            int offset = 0;

            lock (sending_lock_)
            {
                DebugUtils.Assert(sending_.Count >= 2);

                int length = sending_[0].data.Count + sending_[1].data.Count;
                if (length > send_buffer_.Length)
                {
                    send_buffer_ = new byte[length];
                }

                // one header + one body
                for (int i = 0; i < 2; ++i)
                {
                    SendingBuffer item = sending_[i];
                    Buffer.BlockCopy(item.data.Array, 0, send_buffer_, offset, item.data.Count);
                    offset += item.data.Count;
                }
            }

            if (offset > 0)
            {
                if (offset > kUnitBufferSize)
                {
                    Debug.Log("Message is greater than 64KB. It will be truncated.");
                    DebugUtils.Assert(false);
                }

                sock_.BeginSendTo(send_buffer_, 0, offset, SocketFlags.None,
                                  send_ep_, new AsyncCallback(this.SendBytesCb), this);
            }
        }

        private void SendBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("SendBytesCb called.");

            bool failed = false;

            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kSendFailed;
                    last_error_message_ = "sock is null.";
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                lock (sending_lock_)
                {
                    int nSent = sock_.EndSend(ar);
                    DebugUtils.Log("Sent " + nSent + "bytes");

                    DebugUtils.Assert(sending_.Count >= 2);

                    // Removes header and body segment
                    int nToSend = 0;
                    for (int i = 0; i < 2; ++i)
                    {
                        nToSend += sending_[0].data.Count;
                        sending_.RemoveAt(0);
                    }

                    if (nSent > 0 && nSent < nToSend)
                    {
                        Debug.Log("Failed to transfer udp messages.");
                        DebugUtils.Assert(false);
                    }

                    last_error_code_ = ErrorCode.kNone;
                    last_error_message_ = "";

                    if (!SendUnsentMessages())
                        failed = true;
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginSendTo operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in SendBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void ReceiveBytesCb(IAsyncResult ar)
        {
            DebugUtils.Log("ReceiveBytesCb called.");

            bool failed = false;
            try
            {
                if (sock_ == null)
                {
                    last_error_code_ = ErrorCode.kReceiveFailed;
                    last_error_message_ = "sock is null.";
                    DebugUtils.Log(last_error_message_);
                    return;
                }

                lock (receive_lock_)
                {
                    int nRead = sock_.EndReceive(ar);
                    if (nRead > 0)
                    {
                        received_size_ += nRead;
                        DebugUtils.Log("Received " + nRead + " bytes. Buffer has " + (received_size_ - next_decoding_offset_) + " bytes.");
                    }

                    // Decoding a message
                    if (TryToDecodeHeader())
                    {
                        if (TryToDecodeBody() == false)
                        {
                            DebugUtils.LogWarning("Failed to decode body.");
                            DebugUtils.Assert(false);
                        }
                    }
                    else
                    {
                        DebugUtils.LogWarning("Failed to decode header.");
                        DebugUtils.Assert(false);
                    }

                    if (nRead > 0)
                    {
                        // Resets buffer
                        receive_buffer_ = new byte[kUnitBufferSize];
                        received_size_ = 0;
                        next_decoding_offset_ = 0;

                        // Starts another async receive
                        sock_.BeginReceiveFrom(receive_buffer_, received_size_, receive_buffer_.Length - received_size_, SocketFlags.None,
                                               ref receive_ep_, new AsyncCallback(this.ReceiveBytesCb), this);

                        DebugUtils.Log("Ready to receive more. We can receive upto " + receive_buffer_.Length + " more bytes");
                        last_error_code_ = ErrorCode.kNone;
                        last_error_message_ = "";
                    }
                    else
                    {
                        DebugUtils.Log("Socket closed");
                        if (received_size_ - next_decoding_offset_ > 0)
                        {
                            DebugUtils.Log("Buffer has " + (receive_buffer_.Length - received_size_) + " bytes. But they failed to decode. Discarding.");
                        }
                        last_error_code_ = ErrorCode.kReceiveFailed;
                        last_error_message_ = "Can't not receive messages. Maybe the socket is closed.";
                        DebugUtils.Log(last_error_message_);
                        failed = true;
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                Debug.Log("BeginReceiveFrom operation has been Cancelled.\n" + e.ToString());
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReceiveBytesCb: " + e.ToString();
                DebugUtils.Log(last_error_message_);
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }


        protected Socket sock_;
        private IPEndPoint send_ep_;
        private EndPoint receive_ep_;
        #endregion
    }


    // HTTP transport layer
	public class FunapiHttpTransport : FunapiDecodedTransport
    {
        #region public interface
        public FunapiHttpTransport(string hostname_or_ip, UInt16 port, bool https)
        {
            protocol_ = TransportProtocol.kHttp;

            // Url
            host_url_ = https ? "https://" : "http://";
            host_url_ += hostname_or_ip + ":" + port;

            // Version
            host_url_ += "/v" + FunapiVersion.kProtocolVersion + "/";
        }

        public override void Stop()
        {
            if (state_ == State.kUnknown)
                return;

            foreach (WebState state in list_)
            {
                if (state.request != null)
                {
                    state.aborted = true;
                    state.request.Abort();
                }

                if (state.stream != null)
                    state.stream.Close();
            }

            list_.Clear();

            base.Stop();
        }

        public override bool Started
        {
            get { return state_ == State.kConnected; }
        }

        public override bool IsRequestResponse
        {
            get { return true; }
        }

        public override void Update ()
        {
            if (response_time_ <= 0f)
                return;

            response_time_ -= Time.deltaTime;
            if (response_time_ <= 0f)
            {
                RequestFailure();
            }
        }
        #endregion

        #region internal implementation
        protected override void Init()
        {
            state_ = State.kConnected;

            OnStarted();
        }

        protected override void WireSend()
        {
            DebugUtils.Log("Send a Message.");

            try
            {
                lock (sending_lock_)
                {
                    DebugUtils.Assert(sending_.Count >= 2);
                    DebugUtils.Log("Host Url: " + host_url_);

                    SendingBuffer body = sending_[1];

                    // Request
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(host_url_);
                    request.Method = "POST";
                    request.ContentType = "application/x-www-form-urlencoded";
                    request.ContentLength = body.data.Count;

                    // Response
                    WebState state = new WebState();
                    state.request = request;
                    state.msgtype = body.msgtype;
                    state.sending = body.data;
                    list_.Add(state);

                    cur_request_ = state;
                    response_time_ = kResponseTimeout;

                    request.BeginGetRequestStream(new AsyncCallback(RequestStreamCb), state);
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in WireSend: " + e.ToString();
                Debug.Log(last_error_message_);
                RequestFailure();
            }
        }

        private void RequestStreamCb (IAsyncResult ar)
        {
            DebugUtils.Log("RequestStreamCb called.");

            try
            {
                WebState state = (WebState)ar.AsyncState;
                HttpWebRequest request = state.request;

                Stream stream = request.EndGetRequestStream(ar);
                stream.Write(state.sending.Array, 0, state.sending.Count);
                stream.Close();
                DebugUtils.Log("Sent " + state.sending.Count + "bytes");

                request.BeginGetResponse(new AsyncCallback(ResponseCb), state);
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in RequestStreamCb: " + e.ToString();
                Debug.Log(last_error_message_);
                RequestFailure();
            }
        }

        private void ResponseCb (IAsyncResult ar)
        {
            DebugUtils.Log("ResponseCb called.");

            try
            {
                WebState state = (WebState)ar.AsyncState;
                if (state.aborted)
                    return;

                HttpWebResponse response = (HttpWebResponse)state.request.EndGetResponse(ar);
                state.request = null;
                state.response = response;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Stream stream = response.GetResponseStream();
                    state.stream = stream;
                    state.buffer = new byte[kUnitBufferSize];
                    state.read_data = new byte[kUnitBufferSize];
                    state.read_offset = 0;

                    stream.BeginRead(state.buffer, 0, state.buffer.Length, new AsyncCallback(ReadCb), state);
                }
                else
                {
                    DebugUtils.Log("Failed response. status:" + response.StatusDescription);
                    DebugUtils.Assert(false);
                    RequestFailure();
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ResponseCb: " + e.ToString();
                Debug.Log(last_error_message_);
                RequestFailure();
            }
        }

        private void ReadCb (IAsyncResult ar)
        {
            DebugUtils.Log("ReadCb called.");

            try
            {
                WebState state = (WebState)ar.AsyncState;
                int nRead = state.stream.EndRead(ar);

                if (nRead > 0)
                {
                    DebugUtils.Log("We need more bytes for response. Waiting.");
                    if (state.read_offset + nRead > state.read_data.Length)
                    {
                        byte[] temp = new byte[state.read_data.Length + kUnitBufferSize];
                        Buffer.BlockCopy(state.read_data, 0, temp, 0, state.read_offset);
                        state.read_data = temp;
                    }

                    Buffer.BlockCopy(state.buffer, 0, state.read_data, state.read_offset, nRead);
                    state.read_offset += nRead;

                    state.stream.BeginRead(state.buffer, 0, state.buffer.Length, new AsyncCallback(ReadCb), state);
                }
                else
                {
                    if (state.response == null)
                    {
                        DebugUtils.LogWarning("Response instance is null.");
                        DebugUtils.Assert(false);
                        RequestFailure();
                        return;
                    }

                    lock (sending_lock_)
                    {
                        DebugUtils.Assert(sending_.Count >= 2);

                        // Removes header and body segment
                        sending_.RemoveAt(0);
                        sending_.RemoveAt(0);

                        lock (receive_lock_)
                        {
                            // Header
                            byte[] header = state.response.Headers.ToByteArray();
                            string str_header = Encoding.ASCII.GetString(header, 0, header.Length);
                            str_header = str_header.Insert(0, kVersionHeaderField + kHeaderFieldDelimeter + FunapiVersion.kProtocolVersion + kHeaderDelimeter);
                            str_header = str_header.Replace(kLengthHttpHeaderField, kLengthHeaderField);
                            str_header = str_header.Replace("\r", "");
                            header = Encoding.ASCII.GetBytes(str_header);

                            // Checks buffer space
                            int offset = received_size_;
                            received_size_ += header.Length + state.read_offset;
                            CheckReceiveBuffer();

                            // Copy to buffer
                            Buffer.BlockCopy(header, 0, receive_buffer_, offset, header.Length);
                            Buffer.BlockCopy(state.read_data, 0, receive_buffer_, offset + header.Length, state.read_offset);

                            // Decoding a message
                            if (TryToDecodeHeader())
                            {
                                if (TryToDecodeBody() == false)
                                {
                                    DebugUtils.LogWarning("Failed to decode body.");
                                    DebugUtils.Assert(false);
                                }
                            }
                            else
                            {
                                DebugUtils.LogWarning("Failed to decode header.");
                                DebugUtils.Assert(false);
                            }

                            state.stream.Close();
                            state.stream = null;
                            list_.Remove(state);

                            cur_request_ = null;
                            response_time_ = -1f;
                            last_error_code_ = ErrorCode.kNone;
                            last_error_message_ = "";
                        }

                        SendUnsentMessages();
                    }
                }
            }
            catch (Exception e)
            {
                last_error_code_ = ErrorCode.kExceptionError;
                last_error_message_ = "Failure in ReadCb: " + e.ToString();
                Debug.Log(last_error_message_);
                RequestFailure();
            }
        }

        private void RequestFailure ()
        {
            DebugUtils.Log("RequestFailure - state: " + state_);
            if (state_ == State.kUnknown || cur_request_ == null)
            {
                RequestFailureCallback("");
                return;
            }

            WebState state = cur_request_;

            cur_request_ = null;
            response_time_ = -1f;

            if (state.request != null)
            {
                state.aborted = true;
                state.request.Abort();
            }

            if (state.stream != null)
                state.stream.Close();

            list_.Remove(state);

            lock (sending_lock_)
            {
                DebugUtils.Assert(sending_.Count >= 2);

                // Removes header and body segment
                sending_.RemoveAt(0);
                sending_.RemoveAt(0);

                RequestFailureCallback(state.msgtype);

                SendUnsentMessages();
            }
        }
        #endregion


        // Funapi header-related constants.
        private static readonly string kLengthHttpHeaderField = "content-length";

        // waiting time for response
        private static readonly float kResponseTimeout = 30f;    // seconds

        // Delegates
        public delegate void OnRequestFailure(string msg_type);
        public event OnRequestFailure RequestFailureCallback;

        // Response-related.
        class WebState
        {
            public HttpWebRequest request = null;
            public HttpWebResponse response = null;
            public Stream stream = null;
            public byte[] buffer = null;
            public byte[] read_data = null;
            public int read_offset = 0;
            public bool aborted = false;
            public string msgtype;
            public ArraySegment<byte> sending;
        }

        // member variables.
        private string host_url_;
        private float response_time_ = -1f;
        private WebState cur_request_ = null;
        private List<WebState> list_ = new List<WebState>();
    }



    // Driver to use Funapi network plugin.
    public class FunapiNetwork
    {
        #region public interface
        public FunapiNetwork(FunapiTransport transport, FunMsgType type, bool session_reliability,
                             SessionInitHandler on_session_initiated, SessionCloseHandler on_session_closed)
        {
            state_ = State.kUnknown;
            msg_type_ = type;
            recv_type_ = typeof(FunMessage);
            OnSessionInitiated += new SessionInitHandler(on_session_initiated);
            OnSessionClosed += new SessionCloseHandler(on_session_closed);

            AttachTransport(transport);
            SetDefaultProtocol(transport.protocol);

            seq_recvd_ = 0;
            first_receiving_ = true;
            session_reliability_ = session_reliability;
            seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();

            message_handlers_[kNewSessionMessageType] = this.OnNewSession;
            message_handlers_[kSessionClosedMessageType] = this.OnSessionTimedout;
            message_handlers_[kMaintenanceMessageType] = this.OnMaintenanceMessage;
        }

        // Set default protocol
        public void SetDefaultProtocol (TransportProtocol protocol)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);

            default_protocol_ = protocol;
            Debug.Log("SetProtocol - default protocol is '" + protocol + "'.");

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null)
            {
                default_transport_ = transport;
            }
        }

        // Set message protocol
        public void SetMessageProtocol (TransportProtocol protocol, string msg_type)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);
            message_protocols_[msg_type] = protocol;
        }

        public void AttachTransport (FunapiTransport transport)
        {
            DebugUtils.Assert(transport != null);

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(transport.protocol))
                {
                    Debug.LogWarning("AttachTransport - transport of '" + transport.protocol +
                                     "' type already exists. You should call DetachTransport first.");
                    return;
                }

                transport.ConnectTimeoutCallback += new ConnectTimeoutHandler(OnConnectTimeout);
                transport.StartedCallback += new StartedEventHandler(OnTransportStarted);
                transport.StoppedCallback += new StoppedEventHandler(OnTransportStopped);
                transport.ReceivedCallback += new ReceivedEventHandler(OnTransportReceived);

                serializer_ = new FunMessageSerializer ();
                transport.ProtobufHelper = serializer_;

                transports_[transport.protocol] = transport;

                if (default_protocol_ == TransportProtocol.kDefault)
                    default_protocol_ = transport.protocol;

                if (transport.protocol == default_protocol_)
                    default_transport_ = transport;

                if (Started)
                {
                    transport.Start();
                }

                Debug.Log("'" + transport.protocol + "' transport attached.");
            }
        }

        public void DetachTransport (TransportProtocol protocol)
        {
            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                {
                    if (protocol == default_protocol_)
                    {
                        default_protocol_ = TransportProtocol.kDefault;
                        Debug.Log("DetachTransport - Deletes default protocol.");
                    }

                    FunapiTransport transport = transports_[protocol];
                    if (transport != null && transport.Started)
                        transport.Stop();

                    transports_.Remove(protocol);
                    Debug.Log("'" + protocol + "' transport detached.");
                }
                else
                {
                    Debug.LogWarning("DetachTransport - Can't find a transport of '" + protocol + "' type.");
                    DebugUtils.Assert(false);
                }
            }
        }

        public bool HasTransport (TransportProtocol protocol)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return true;
            }

            return false;
        }

        public FunMessage CreateFunMessage(object msg, int msg_index)
        {
            FunMessage _msg = new FunMessage();
            Extensible.AppendValue(serializer_, _msg, msg_index, ProtoBuf.DataFormat.Default, msg);
            return _msg;
        }

        public object GetMessage(FunMessage msg, Type msg_type, int msg_index)
        {
            object _msg = null;
            bool success = Extensible.TryGetValue(serializer_, msg_type, msg,
                                                  msg_index, ProtoBuf.DataFormat.Default, true, out _msg);
            if (!success)
            {
                Debug.Log(String.Format("Failed to decode {0} {1}", msg_type, msg_index));
                return null;
            }

            return _msg;
        }

        public void Start()
        {
            state_ = State.kStarted;
            Debug.Log("Starting a network module.");

            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    transport.Start();
                }
            }
        }

        public void Stop()
        {
            // Waits for unsent messages.
            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.Started && transport.HaveUnsentMessages)
                    {
                        state_ = State.WaitForStop;
                        return;
                    }
                }
            }

            Debug.Log("Stopping a network module.");
            state_ = State.kUnknown;

            StopTransport();
            transports_.Clear();

            CloseSession();
        }

        public void StopTransport()
        {
            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport.Started)
                        transport.Stop();
                }
            }
        }

        // Your update method inheriting MonoBehaviour should explicitly invoke this method.
        public void Update ()
        {
            lock (transports_lock_)
            {
                foreach (FunapiTransport transport in transports_.Values)
                {
                    if (transport != null)
                        transport.Update();
                }
            }

            if (state_ == State.WaitForStop)
            {
                Stop();
                return;
            }

            lock (message_lock_)
            {
                if (message_buffer_.Count > 0)
                {
                    DebugUtils.Log("Update messages. count: " + message_buffer_.Count);

                    try
                    {
                        string msg_type;
                        foreach (KeyValuePair<TransportProtocol, ArraySegment<byte>> buffer in message_buffer_)
                        {
                            msg_type = ProcessMessage(buffer.Key, buffer.Value);

                            if (expected_replies_.ContainsKey(msg_type))
                            {
                                expected_replies_.Remove(msg_type);
                            }
                        }

                        message_buffer_.Clear();
                    }
                    catch (Exception e)
                    {
                        Debug.Log("Failure in Update: " + e.ToString());
                    }
                }
            }

            if (expected_replies_.Count > 0)
            {
                List<string> remove_list = new List<string>();

                foreach (var item in expected_replies_)
                {
                    item.Value.wait_time -= Time.deltaTime;
                    if (item.Value.wait_time <= 0f)
                    {
                        Debug.Log("'" + item.Key + "' message waiting time has been exceeded.");
                        remove_list.Add(item.Key);
                        item.Value.callback(item.Key);
                    }
                }

                if (remove_list.Count > 0)
                {
                    foreach (string key in remove_list)
                    {
                        expected_replies_.Remove(key);
                    }
                }
            }
        }

        public bool Started
        {
            get { return state_ != State.kUnknown; }
        }

        public bool Connected
        {
            get { return default_transport_ != null && default_transport_.Started; }
        }

        public bool SessionReliability
        {
            get { return session_reliability_; }
        }

        public FunMsgType MsgType
        {
            get { return msg_type_; }
        }


        public void SendMessage(string msg_type, FunMessage message)
        {
            SendMessage(msg_type, message, GetProtocol(msg_type));
        }

        public void SendMessage(string msg_type, FunMessage message, TransportProtocol protocol)
        {
            DebugUtils.Assert(msg_type_ == FunMsgType.kProtobuf);
            bool transport_reliability = (protocol == TransportProtocol.kTcp && session_reliability_);

            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                DebugUtils.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            message.msgtype = msg_type;

            // Encodes a session id, if any.
            if (session_id_ != null && session_id_.Length > 0)
            {
                message.sid = session_id_;
            }

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null && state_ == State.kEstablished &&
                (transport_reliability == false || unsent_queue_.Count <= 0))
            {
                if (transport_reliability)
                {
                    message.seq = seq_;
                    ++seq_;

                    send_queue_.Enqueue(message);
                }

                transport.SendMessage(message);
            }
            else if (transport_reliability ||
                     (transport != null && transport.state == FunapiTransport.State.kConnected))
            {
                unsent_queue_.Enqueue(new UnsentMessage(message, protocol));
                Debug.Log("SendMessage - '" + msg_type + "' message queued.");
            }
            else
            {
                string str_log = "SendMessage - '" + msg_type + "' message skipped.";
                if (transport == null)
                    str_log += "\nThere's no '" + protocol + "' transport.";
                else if (transport.state != FunapiTransport.State.kConnected)
                    str_log += "\nTransport's state is '" + transport.state + "'.";

                Debug.Log(str_log);
            }
        }

        public void SendMessage(string msg_type, FunMessage message,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, message, GetProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, FunMessage message, TransportProtocol protocol,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            if (expected_replies_.ContainsKey(message.msgtype))
            {
                DebugUtils.Log("ERROR: Dictionary has the same key already exists. key: " + message.msgtype);
                DebugUtils.Assert(false);
            }

            expected_replies_[expected_reply_type] = new ExpectedReplyMessage(expected_reply_time, onReplyMissed);

            SendMessage(msg_type, message, protocol);
        }

        public void SendMessage(string msg_type, object body)
        {
            SendMessage(msg_type, body, GetProtocol(msg_type));
        }

        public void SendMessage(string msg_type, object body, TransportProtocol protocol)
        {
            DebugUtils.Assert(msg_type_ == FunMsgType.kJson);
            bool transport_reliability = (protocol == TransportProtocol.kTcp && session_reliability_);

            // Invalidates session id if it is too stale.
            if (last_received_.AddSeconds(kFunapiSessionTimeout) < DateTime.Now)
            {
                DebugUtils.Log("Session is too stale. The server might have invalidated my session. Resetting.");
                session_id_ = "";
            }

            // Encodes a messsage type
            json_helper_.SetStringField(body, kMsgTypeBodyField, msg_type);

            // Encodes a session id, if any.
            if (session_id_ != null && session_id_.Length > 0)
            {
                json_helper_.SetStringField(body, kSessionIdBodyField, session_id_);
            }

            FunapiTransport transport = GetTransport(protocol);
            if (transport != null && state_ == State.kEstablished &&
                (transport_reliability == false || unsent_queue_.Count <= 0))
            {
                if (transport_reliability)
                {
                    transport.JsonHelper.SetIntegerField(body, kSeqNumberField, seq_);
                    ++seq_;

                    send_queue_.Enqueue(json_helper_.Clone(body));
                }

                transport.SendMessage(msg_type, body);
            }
            else if (transport_reliability ||
                     (transport != null && transport.state == FunapiTransport.State.kConnected))
            {
                unsent_queue_.Enqueue(new UnsentMessage(json_helper_.Clone(body), protocol));
                Debug.Log("SendMessage - '" + msg_type + "' message queued.");
            }
            else
            {
                string str_log = "SendMessage - '" + msg_type + "' message skipped.";
                if (transport == null)
                    str_log += "\nThere's no '" + protocol + "' transport.";
                else if (transport.state != FunapiTransport.State.kConnected)
                    str_log += "\nTransport's state is '" + transport.state + "'.";

                Debug.Log(str_log);
            }
        }

        public void SendMessage(string msg_type, object body,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            SendMessage(msg_type, body, GetProtocol(msg_type),
                        expected_reply_type, expected_reply_time, onReplyMissed);
        }

        public void SendMessage(string msg_type, object body, TransportProtocol protocol,
                                string expected_reply_type, float expected_reply_time, TimeoutEventHandler onReplyMissed)
        {
            if (expected_replies_.ContainsKey(msg_type))
            {
                DebugUtils.Log("ERROR: Dictionary has the same key already exists. key: " + msg_type);
                DebugUtils.Assert(false);
            }

            expected_replies_[expected_reply_type] =  new ExpectedReplyMessage(expected_reply_time, onReplyMissed);

            SendMessage(msg_type, body, protocol);
        }

        private void SendUnsentMessages()
        {
            if (unsent_queue_.Count <= 0)
                return;

            Debug.Log("SendUnsentMessages - " + unsent_queue_.Count + " unsent messages.");

            foreach (UnsentMessage msg in unsent_queue_)
            {
                FunapiTransport transport = GetTransport(msg.protocol);
                if (transport == null)
                {
                    Debug.Log("SendUnsentMessages - Can't find a '" + msg.protocol + "' transport.\n" +
                              "message skipped.");
                    continue;
                }

                if (msg_type_ == FunMsgType.kJson)
                {
                    object json = msg.message;
                    string msgtype = transport.JsonHelper.GetStringField(json, kMsgTypeBodyField) as string;
                    if (session_id_ != null && session_id_.Length > 0)
                        transport.JsonHelper.SetStringField(json, kSessionIdBodyField, session_id_);

                    transport.SendMessage(msgtype, msg);
                }
                else if (msg_type_ == FunMsgType.kProtobuf)
                {
                    FunMessage message = msg.message as FunMessage;
                    if (session_id_ != null && session_id_.Length > 0)
                        message.sid = session_id_;

                    if (session_reliability_ && transport.protocol == TransportProtocol.kTcp)
                    {
                        message.seq = seq_;
                        ++seq_;
                    }

                    transport.SendMessage(message);
                }
                else
                {
                    DebugUtils.Assert(false);
                }
            }

            unsent_queue_.Clear();
        }

        public void RegisterHandler(string type, MessageEventHandler handler)
        {
            DebugUtils.Log("New handler for message type '" + type + "'");
            message_handlers_[type] = handler;
        }

        public void RegisterHandlerWithProtocol(string type, TransportProtocol protocol, MessageEventHandler handler)
        {
            if (protocol == TransportProtocol.kDefault)
            {
                RegisterHandler(type, handler);
                return;
            }

            DebugUtils.Log("New handler for and message type '" + type + "' of '" + protocol + "' protocol.");
            message_protocols_[type] = protocol;
            message_handlers_[type] = handler;
        }

        public ErrorCode last_error_code_
        {
            get
            {
                if (default_transport_ != null)
                    return default_transport_.LastErrorCode;

                return ErrorCode.kNone;
            }
        }

        public string last_error_message_
        {
            get
            {
                if (default_transport_ != null)
                    return default_transport_.LastErrorMessage;

                return "";
            }
        }
        #endregion

        #region internal implementation
        private void PrepareSession(string session_id)
        {
            if (session_id_.Length == 0)
            {
                DebugUtils.Log("New session id: " + session_id);
                OpenSession(session_id);
            }

            if (session_id_ != session_id)
            {
                DebugUtils.Log("Session id changed: " + session_id_ + " => " + session_id);

                CloseSession();
                OpenSession(session_id);
            }
        }

        private void OpenSession(string session_id)
        {
            DebugUtils.Assert(session_id_.Length == 0);

            state_ = State.kEstablished;
            session_id_ = session_id;

            OnSessionInitiated(session_id_);

            if (unsent_queue_.Count > 0)
            {
                SendUnsentMessages();
            }
        }

        private void CloseSession()
        {
            if (session_id_.Length == 0)
                return;

            state_ = State.kUnknown;
            session_id_ = "";

            if (session_reliability_)
            {
                seq_recvd_ = 0;
                first_receiving_ = true;
                send_queue_.Clear();
                seq_ = (UInt32)rnd_.Next() + (UInt32)rnd_.Next();
            }

            OnSessionClosed();
        }

        private TransportProtocol GetProtocol (string msg_type)
        {
            if (message_protocols_.ContainsKey(msg_type))
                return message_protocols_[msg_type];

            return default_protocol_;
        }

        private FunapiTransport GetTransport (TransportProtocol protocol)
        {
            DebugUtils.Assert(protocol != TransportProtocol.kDefault);

            lock (transports_lock_)
            {
                if (transports_.ContainsKey(protocol))
                    return transports_[protocol];
            }

            return null;
        }

        private void OnTransportReceived (TransportProtocol protocol, Dictionary<string, string> header, ArraySegment<byte> body)
        {
            DebugUtils.Log("OnTransportReceived invoked.");
            last_received_ = DateTime.Now;

            lock (message_lock_)
            {
                message_buffer_.Add(new KeyValuePair<TransportProtocol, ArraySegment<byte>>(protocol, body));
            }
        }

        private string ProcessMessage (TransportProtocol protocol, ArraySegment<byte> buffer)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
                return "";

            string msg_type = "";
            string session_id = "";

            if (msg_type_ == FunMsgType.kJson)
            {
                string str = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
                object json = transport.JsonHelper.Deserialize(str);
                DebugUtils.Log("Parsed json: " + str);

                DebugUtils.Assert(transport.JsonHelper.GetStringField(json, kSessionIdBodyField) is string);
                string session_id_node = transport.JsonHelper.GetStringField(json, kSessionIdBodyField) as string;
                session_id = session_id_node;
                transport.JsonHelper.RemoveStringField(json, kSessionIdBodyField);

                PrepareSession(session_id);

                if (protocol == TransportProtocol.kTcp && session_reliability_)
                {
                    if (transport.JsonHelper.HasField(json, kAckNumberField))
                    {
                        UInt32 ack = (UInt32)transport.JsonHelper.GetIntegerField(json, kAckNumberField);
                        OnAckReceived(ack);
                        // Does not support piggybacking.
                        DebugUtils.Assert(!transport.JsonHelper.HasField(json, kMsgTypeBodyField));
                        return msg_type;
                    }

                    if (transport.JsonHelper.HasField(json, kSeqNumberField))
                    {
                        UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(json, kSeqNumberField);
                        if (!OnSeqReceived(seq))
                        {
                            return msg_type;
                        }
                        transport.JsonHelper.RemoveStringField(json, kSeqNumberField);
                    }
                }

                if (transport.JsonHelper.HasField(json, kMsgTypeBodyField))
                {
                    string msg_type_node = transport.JsonHelper.GetStringField(json, kMsgTypeBodyField) as string;
                    msg_type = msg_type_node;
                    transport.JsonHelper.RemoveStringField(json, kMsgTypeBodyField);

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, json);
                }
            }
            else if (msg_type_ == FunMsgType.kProtobuf)
            {
                MemoryStream stream = new MemoryStream(buffer.Array, buffer.Offset, buffer.Count, false);
                FunMessage message = (FunMessage)serializer_.Deserialize(stream, null, recv_type_);

                session_id = message.sid;

                PrepareSession(session_id);

                if (protocol == TransportProtocol.kTcp && session_reliability_)
                {
                    if (message.ackSpecified)
                    {
                        OnAckReceived(message.ack);
                        // Does not support piggybacking.
                        return msg_type;
                    }

                    if (message.seqSpecified)
                    {
                        if (!OnSeqReceived(message.seq))
                        {
                            return msg_type;
                        }
                    }
                }

                if (message.msgtype != null && message.msgtype.Length > 0)
                {
                    msg_type = message.msgtype;

                    if (message_handlers_.ContainsKey(msg_type))
                        message_handlers_[msg_type](msg_type, message);
                }
            }
            else
            {
                Debug.Log("Invalid message type. type: " + msg_type_);
                DebugUtils.Assert(false);
                return msg_type;
            }

            if (!message_handlers_.ContainsKey(msg_type))
            {
                if (session_id_.Length > 0 && state_ == State.kWaitForAck)
                {
                    state_ = State.kEstablished;
                }

                Debug.Log("No handler for message '" + msg_type + "'. Ignoring.");
            }

            return msg_type;
        }

        private bool SeqLess(UInt32 x, UInt32 y)
        {
            Int32 dist = (Int32)(x - y);
            return dist > 0;
        }

        private void SendAck(UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);

            FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
            if (transport == null)
                return;

            if (msg_type_ == FunMsgType.kJson)
            {
                object ack_msg = transport.JsonHelper.Deserialize("{}");
                transport.JsonHelper.SetStringField(ack_msg, kSessionIdBodyField, session_id_);
                transport.JsonHelper.SetIntegerField(ack_msg, kAckNumberField, ack);
                transport.SendMessage("", ack_msg);
            }
            else
            {
                FunMessage ack_msg = new FunMessage();
                ack_msg.sid = session_id_;
                ack_msg.ack = ack;
                transport.SendMessage(ack_msg);
            }
        }

        private void SendEmptyMessage (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            if (transport == null)
            {
                Debug.Log("SendEmptyMessage - transport is null.");
                return;
            }

            if (msg_type_ == FunMsgType.kJson)
            {
                object ack_msg = transport.JsonHelper.Deserialize("{}");
                transport.SendMessage("", ack_msg);
            }
            else
            {
                FunMessage ack_msg = new FunMessage();
                transport.SendMessage(ack_msg);
            }
        }

        private bool OnSeqReceived(UInt32 seq)
        {
            if (first_receiving_)
            {
                first_receiving_ = false;
            }
            else
            {
                if (seq_recvd_ + 1 != seq)
                {
                    DebugUtils.Log("Received wrong sequence number " + seq.ToString() +
                                   ".(" + (seq_recvd_ + 1).ToString() + " expected");
                    DebugUtils.Assert(false);
                    Stop();
                    return false;
                }
            }

            seq_recvd_ = seq;
            SendAck(seq_recvd_ + 1);
            return true;
        }

        private void OnAckReceived(UInt32 ack)
        {
            DebugUtils.Assert(session_reliability_);

            FunapiTransport transport = GetTransport(TransportProtocol.kTcp);
            if (transport == null)
            {
                Debug.LogError("OnAckReceived - transport is null.");
                return;
            }

            while (send_queue_.Count > 0)
            {
                UInt32 seq;
                object last_msg = send_queue_.Peek();
                if (msg_type_ == FunMsgType.kJson)
                {
                    seq = (UInt32)transport.JsonHelper.GetIntegerField(last_msg, kSeqNumberField);
                }
                else if (msg_type_ == FunMsgType.kProtobuf)
                {
                    seq = (last_msg as FunMessage).seq;
                }
                else
                {
                    DebugUtils.Assert(false);
                    seq = 0;
                }

                if (SeqLess(ack, seq))
                {
                    send_queue_.Dequeue();
                }
                else
                {
                    break;
                }
            }

            if (state_ == State.kWaitForAck)
            {
                foreach (object msg in send_queue_)
                {
                    if (msg_type_ == FunMsgType.kJson)
                    {
                        UInt32 seq = (UInt32)transport.JsonHelper.GetIntegerField(msg, kSeqNumberField);
                        DebugUtils.Assert(seq == ack || SeqLess(seq, ack));

                        string msgtype = transport.JsonHelper.GetStringField(msg, kMsgTypeBodyField) as string;
                        transport.SendMessage(msgtype, msg);
                    }
                    else if (msg_type_ == FunMsgType.kProtobuf)
                    {
                        UInt32 seq = (msg as FunMessage).seq;
                        DebugUtils.Assert(seq == ack || SeqLess (seq, ack));
                        transport.SendMessage(msg as FunMessage);
                    }
                    else
                    {
                        DebugUtils.Assert(false);
                    }
                }

                state_ = State.kEstablished;
            }
        }

        private void OnConnectTimeout (TransportProtocol protocol)
        {
            if (protocol != TransportProtocol.kTcp || session_reliability_ == false)
            {
                FunapiTransport transport = GetTransport(protocol);
                if (transport != null)
                    transport.Stop();
            }
        }

        private void OnTransportStarted (TransportProtocol protocol)
        {
            FunapiTransport transport = GetTransport(protocol);
            DebugUtils.Assert(transport != null);
            Debug.Log("'" + protocol + "' Transport started.");

            if (session_id_ != null && session_id_.Length > 0)
            {
                if (session_reliability_ && protocol == TransportProtocol.kTcp && seq_recvd_ != 0)
                {
                    state_ = State.kWaitForAck;
                    SendAck(seq_recvd_ + 1);
                }
                else
                {
                    state_ = State.kEstablished;
                }
            }
            else if (state_ == State.kStarted)
            {
                state_ = State.kWaitForSession;

                // To get a session id
                SendEmptyMessage(protocol);
            }
        }

        private void OnTransportStopped (TransportProtocol protocol)
        {
            Debug.Log("'" + protocol + "' Transport terminated. Stopping.");
        }
        #endregion

        #region Funapi system message handlers
        private void OnNewSession(string msg_type, object body)
        {
            // ignore.
        }

        private void OnSessionTimedout(string msg_type, object body)
        {
            Debug.Log("Session timed out. Resetting my session id. The server will send me another one next time.");

            CloseSession();
        }

        private void OnMaintenanceMessage(string msg_type, object body)
        {
            MaintenanceCallback(msg_type, body);
        }
        #endregion


        // Status
        public enum State
        {
            kUnknown = 0,
            kStarted,
            kWaitForAck,
            kWaitForSession,
            kEstablished,
            WaitForStop
        };

        // Delegates
        public delegate void MessageEventHandler(string msg_type, object body);
        public delegate void TimeoutEventHandler(string msg_type);
        public delegate void SessionInitHandler(string session_id);
        public delegate void SessionCloseHandler();

        // Unsent queue-releated class
        class UnsentMessage
        {
            public UnsentMessage(object message, TransportProtocol protocol)
            {
                this.message = message;
                this.protocol = protocol;
            }

            public object message;
            public TransportProtocol protocol;
        }

        // Callback timer for expected reply messages
        class ExpectedReplyMessage
        {
            public ExpectedReplyMessage (float time, TimeoutEventHandler cb)
            {
                wait_time = time;
                callback = cb;
            }

            public float wait_time;
            public TimeoutEventHandler callback;
        }

        // Funapi message-related events.
        public event SessionInitHandler OnSessionInitiated;
        public event SessionCloseHandler OnSessionClosed;
        public event MessageEventHandler MaintenanceCallback;

        // Funapi message-related constants.
        private static readonly float kFunapiSessionTimeout = 3600.0f;
        private static readonly string kMsgTypeBodyField = "_msgtype";
        private static readonly string kSessionIdBodyField = "_sid";
        private static readonly string kSeqNumberField = "_seq";
        private static readonly string kAckNumberField = "_ack";
        private static readonly string kNewSessionMessageType = "_session_opened";
        private static readonly string kSessionClosedMessageType = "_session_closed";
        private static readonly string kMaintenanceMessageType = "_maintenance";

        // Member variables.
        private State state_;
        private Type recv_type_;
        private FunMsgType msg_type_;
        private TransportProtocol default_protocol_ = TransportProtocol.kDefault;
        private FunMessageSerializer serializer_;
        private FunapiTransport default_transport_ = null;
        private Dictionary<TransportProtocol, FunapiTransport> transports_ = new Dictionary<TransportProtocol, FunapiTransport>();
        private string session_id_ = "";
        private Dictionary<string, TransportProtocol> message_protocols_ = new Dictionary<string, TransportProtocol>();
        private Dictionary<string, MessageEventHandler> message_handlers_ = new Dictionary<string, MessageEventHandler>();
        private Dictionary<string, ExpectedReplyMessage> expected_replies_ = new Dictionary<string, ExpectedReplyMessage>();
        private List<KeyValuePair<TransportProtocol, ArraySegment<byte>>> message_buffer_ = new List<KeyValuePair<TransportProtocol, ArraySegment<byte>>>();
        private object message_lock_ = new object();
        private object transports_lock_ = new object();
        private DateTime last_received_ = DateTime.Now;
        private JsonAccessor json_helper_ = new DictionaryJsonAccessor();

        // Reliability-releated member variables.
        private bool session_reliability_;
        private UInt32 seq_;
        private UInt32 seq_recvd_;
        private bool first_receiving_;
        private System.Collections.Queue send_queue_ = new System.Collections.Queue();
        private System.Collections.Queue unsent_queue_ = new System.Collections.Queue();
        private System.Random rnd_ = new System.Random();
    }
}  // namespace Fun
