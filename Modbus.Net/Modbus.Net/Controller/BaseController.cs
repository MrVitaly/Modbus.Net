using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Modbus.Net
{
    /// <summary>
    ///     控制器基类
    /// </summary>
    public abstract class BaseController : IController
    {
        /// <summary>
        ///     等待的消息队列
        /// </summary>
        protected List<MessageWaitingDef> WaitingMessages { get; set; }

        /// <summary>
        ///     消息维护线程
        /// </summary>
        protected Task SendingThread { get; set; }

        /// <summary>
        ///     包切分位置函数
        /// </summary>
        protected Func<byte[], int> LengthCalc { get; }

        /// <summary>
        ///     包校验函数
        /// </summary>
        protected Func<byte[], bool?> CheckRightFunc { get; }

        /// <summary>
        ///     构造器
        /// </summary>
        /// <param name="lengthCalc">包长度计算函数</param>
        /// <param name="checkRightFunc">包校验函数</param>
        protected BaseController(Func<byte[], int> lengthCalc = null, Func<byte[], bool?> checkRightFunc = null)
        {
            WaitingMessages = new List<MessageWaitingDef>();
            LengthCalc = lengthCalc;
            CheckRightFunc = checkRightFunc;
        }

        /// <inheritdoc />
        public MessageWaitingDef AddMessage(byte[] sendMessage)
        {
            var def = new MessageWaitingDef
            {
                Key = GetKeyFromMessage(sendMessage)?.Item1,
                SendMessage = sendMessage,
                SendMutex = new AutoResetEvent(false),
                ReceiveMutex = new AutoResetEvent(false)
            };
            if (AddMessageToList(def))
            {
                return def;
            }
            return null;
        }

        /// <summary>
        ///     发送消息的实际内部方法
        /// </summary>
        protected abstract void SendingMessageControlInner();

        /// <inheritdoc />
        public abstract void SendStop();

        /// <inheritdoc />
        public virtual void SendStart()
        {
            if (SendingThread == null)
            {
                SendingThread = Task.Run(() => SendingMessageControlInner());
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (WaitingMessages)
            {
                WaitingMessages.Clear();
            }
        }

        /// <summary>
        ///     将信息添加到队列
        /// </summary>
        /// <param name="def">需要添加的信息信息</param>
        protected virtual bool AddMessageToList(MessageWaitingDef def)
        {
            lock (WaitingMessages)
            {
                if (WaitingMessages.FirstOrDefault(p => p.Key == def.Key) == null || def.Key == null)
                {
                    WaitingMessages.Add(def);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        ///     获取信息的检索关键字
        /// </summary>
        /// <param name="message">待确认的信息</param>
        /// <returns>信息的检索关键字</returns>
        protected abstract (string, string)? GetKeyFromMessage(byte[] message);

        /// <inheritdoc />
        public ICollection<(byte[], bool)> ConfirmMessage(byte[] receiveMessage)
        {
            var ans = new List<(byte[], bool)>();
            byte[] receiveMessageCopy = new byte[receiveMessage.Length];
            Array.Copy(receiveMessage, receiveMessageCopy, receiveMessage.Length);
            var length = LengthCalc?.Invoke(receiveMessageCopy);
            List<byte[]> duplicatedMessages;
            if (length == null || length == -1) return ans;
            if (length == 0) return null;
            else
            {
                duplicatedMessages = new List<byte[]>();
                while (receiveMessageCopy.Length >= length)
                {
                    var duplicateMessage = receiveMessageCopy.Take(length.Value).ToArray();
                    if (CheckRightFunc != null && CheckRightFunc(duplicateMessage) == false)
                    {
                        receiveMessageCopy = receiveMessageCopy.TakeLast(receiveMessage.Length - 1).ToArray();
                        continue;
                    }
                    duplicatedMessages.Add(duplicateMessage);
                    receiveMessageCopy = receiveMessageCopy.TakeLast(receiveMessage.Length - length.Value).ToArray();
                    if (receiveMessageCopy.Length == 0) break;
                    length = LengthCalc?.Invoke(receiveMessageCopy);
                    if (length == -1) break;
                    if (length == 0) return null;
                }
            }
            foreach (var message in duplicatedMessages)
            {
                var def = GetMessageFromWaitingList(message);
                if (def != null)
                {
                    def.ReceiveMessage = receiveMessage;
                    lock (WaitingMessages)
                    {
                        WaitingMessages.Remove(def);
                    }
                    def.ReceiveMutex.Set();
                    ans.Add((message, true));
                }
                else
                {
                    ans.Add((message, false));
                }
            }
            return ans;
        }

        /// <summary>
        ///     从等待队列中匹配信息
        /// </summary>
        /// <param name="receiveMessage">返回的信息</param>
        /// <returns>从等待队列中匹配的信息</returns>
        protected abstract MessageWaitingDef GetMessageFromWaitingList(byte[] receiveMessage);

        /// <inheritdoc />
        public void ForceRemoveWaitingMessage(MessageWaitingDef def)
        {
            lock (WaitingMessages)
            {
                WaitingMessages.Remove(def);
            }
        }
    }

    /// <summary>
    ///     等待信息的定义
    /// </summary>
    public class MessageWaitingDef
    {
        /// <summary>
        ///     信息的关键字
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        ///     发送的信息
        /// </summary>
        public byte[] SendMessage { get; set; }

        /// <summary>
        ///     接收的信息
        /// </summary>
        public byte[] ReceiveMessage { get; set; }

        /// <summary>
        ///     发送的信号
        /// </summary>
        public EventWaitHandle SendMutex { get; set; }

        /// <summary>
        ///     接收的信号
        /// </summary>
        public EventWaitHandle ReceiveMutex { get; set; }
    }
}