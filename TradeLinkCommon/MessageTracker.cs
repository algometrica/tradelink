﻿using System;
using System.Collections.Generic;
using System.Text;
using TradeLink.API;


namespace TradeLink.Common
{
    public class MessageTracker
    {
        TLClient _tl = null;
        /// <summary>
        /// create a message tracker
        /// </summary>
        public MessageTracker() : this(null) { }
        /// <summary>
        /// create a message tracker that communicates with a TL client
        /// </summary>
        /// <param name="tl"></param>
        public MessageTracker(TLClient tl)
        {
            _tl = tl;
        }
        public event StringDecimalDelegate GotOpenPrice;
        public event StringDecimalDelegate GotClosePrice;
        public event StringDecimalDelegate GotHighPrice;
        public event StringDecimalDelegate GotLowPrice;
        public event MessageArrayDel GotFeatures;
        public event DebugDelegate SendDebug;
        public event MessageDelegate SendMessageResponse;
        public event ProviderDelegate GotProvider;
        public virtual bool GotMessage(MessageTypes type, uint source, uint dest, uint msgid, string request, ref string response)
        {
            decimal v = 0;
            long lv = 0;
            switch (type)
            {
                case MessageTypes.CLOSEPRICE:
                    {
                        if ((GotClosePrice != null) && long.TryParse(response, out lv))
                        {
                            GotClosePrice(request, WMUtil.unpack(lv));
                        }
                        return true;
                    }
                case MessageTypes.OPENPRICE:
                    {
                        if ((GotOpenPrice != null) && long.TryParse(response, out lv))
                            GotOpenPrice(request, WMUtil.unpack(lv));
                        return true;
                    }
                case MessageTypes.DAYHIGH:
                    {
                        if ((GotHighPrice != null) && long.TryParse(response, out lv))
                            GotHighPrice(request, WMUtil.unpack(lv));
                        return true;
                    }
                case MessageTypes.DAYLOW:
                    {
                        if ((GotLowPrice != null) && long.TryParse(response, out lv))
                            GotLowPrice(request, WMUtil.unpack(lv));
                        return true;
                    }
                case MessageTypes.BROKERNAME:
                    {
                        if (GotProvider != null)
                        {
                            try
                            {
                                Providers p = (Providers)Enum.Parse(typeof(Providers), response);
                                GotProvider(p);
                            }
                            catch (Exception ex)
                            {
                                debug("Unknown provider: " + response);
                                debug(ex.Message + ex.StackTrace);
                                return false;
                            }
                        }
                        return true;
                    }
                case MessageTypes.FEATURERESPONSE:
                    {
                        if (GotFeatures != null)
                        {
                            string[] r = response.Split(delim);
                            List<MessageTypes> f = new List<MessageTypes>();
                            foreach (string rs in r)
                            {
                                try
                                {
                                    MessageTypes mt = (MessageTypes)Enum.Parse(typeof(MessageTypes), rs);
                                    f.Add(mt);
                                }
                                catch { continue; }
                            }
                            if (f.Count > 0)
                                GotFeatures(f.ToArray());
                        }
                        return true;
                    }
            }
            return false;
        }

        public virtual bool SendMessage(MessageTypes type, uint source, uint dest, uint msgid, string request, string response)
        {
            if (_tl == null)return false;
            if (!_tl.RequestFeatureList.Contains(type))
            {
                SendDebug(type.ToString() + " not supported by " + _tl.Name);
                return false;
            }
            try
            {
                // prepare message
                switch (type)
                {
                    case MessageTypes.DOMREQUEST:
                        request.Replace(Book.EMPTYREQUESTOR, _tl.Name);
                        break;
                }
                // send it
                long result = _tl.TLSend(type, request);
                string res = result.ToString();
                // pass along result
                if ((SendMessageResponse != null) && (result != 0))
                    SendMessageResponse(type, source, dest, msgid, request, ref res);
                return true;
            }
            catch (Exception ex)
            {
                debug("Error on: "+type.ToString()+ source + dest + msgid + request + response);
                debug(ex.Message + ex.StackTrace);
            }
            return false;
        }
        

        void debug(string m)
        {
            if (SendDebug != null)
                SendDebug(m);
        }

        public const char delim = '+';
        public const int PARAM1 = 0;
        public const int PARAM2 = 1;
        public const int PARAM3 = 1;
        public static string[] ParseRequest(string request)
        {
            return request.Split(delim);
        }
        public static string RequestParam(string request,int param) { return ParseRequest(request)[param]; }
        public static string BuildParams(string[] param)
        {
            return string.Join(delim.ToString(), param);
        }
    }
}