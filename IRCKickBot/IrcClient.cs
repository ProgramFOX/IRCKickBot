﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Globalization;

namespace IRCKickBot
{
    class IrcClient
    {
        TcpClient _client = new TcpClient();
        NetworkStream _stream;
        bool shouldClose = false;
        HashSet<string> _joinedChannels = new HashSet<string>();
        Configuration config = null;

        public IrcClient(Configuration config)
        {
            this.config = config;
        }

        public void ConnectAndJoin()
        {
            _client.Connect(config.Host, config.Port.Value);
            _stream = _client.GetStream();
            if (!String.IsNullOrEmpty(config.Password))
            {
                Send("PASS " + config.Password);
            }
            Send("NICK " + config.Nickname);
            Send("USER " + config.Nickname + " 0 * " + config.Nickname);
            JoinChannels();
        }

        private void JoinChannels()
        {
            string[] channelList = config.Channels.Split(',');
            foreach (string c in channelList)
            {
                _joinedChannels.Add(c.Trim());
            }
            Send("JOIN " + config.Channels);
        }

        public void Send(string command)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(command + "\r\n");
            _stream.Write(buffer, 0, buffer.Length);
        }

        private void HandlePings(string line)
        {
            if (line.StartsWith("PING", StringComparison.Ordinal) && line[4] == ' ')
            {
                string server = line.Split(' ')[1];
                Send("PONG " + server);
            }
        }

        public void ReceiveLoop()
        {
            using (StreamReader sr = new StreamReader(_stream))
            {
                string line;
                try
                {
                    while (!shouldClose && (line = sr.ReadLine()) != null)
                    {
                        Console.WriteLine(line);
                        HandlePings(line);
                        if (!line.StartsWith(":", StringComparison.Ordinal))
                            continue;
                        string[] parts = line.Remove(0, 1).Split(' ');
                        string user = parts[0].Split('!')[0];
                        if (parts[1] != "PRIVMSG")
                        {
                            continue;
                        }
                        string posted = string.Join(" ", parts.Skip(3)).Remove(0, 1);
                        bool kicked = false;
                        foreach (KickPattern pattern in config.Patterns)
                        {
                            if (_joinedChannels.Contains(parts[2]) && pattern.RegularExpression.IsMatch(posted))
                            {
                                Send(string.Format(CultureInfo.InvariantCulture, "KICK {0} {1} :{2}", parts[2], user, pattern.Reason));
                                kicked = true;
                                break;
                            }
                        }
                        if (kicked)
                        {
                            continue; // To avoid that the next foreach loop gets entered.
                        }
                        foreach (Plugin plugin in config.Plugins)
                        {
                            string kickReason = plugin.ShouldKick(posted);
                            if (kickReason != null)
                            {
                                Send(string.Format(CultureInfo.InvariantCulture, "KICK {0} {1} :{2}", parts[2], user, kickReason));
                                break;
                            }
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (!shouldClose)
                    {
                        throw;
                    }
                }
                catch (IOException)
                {
                    if (!shouldClose)
                    {
                        throw;
                    }
                }
            }
        }

        public void Close()
        {
            shouldClose = true;
            Send("QUIT :Owner requested quit");
            _client.Close();
        }
    }
}
