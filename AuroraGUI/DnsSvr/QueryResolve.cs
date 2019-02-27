﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using LeafDNS.Tools;
using MojoUnity;
using static LeafDNS.Tools.MyTools;

// ReSharper disable CollectionNeverUpdated.Global
#pragma warning disable 649
#pragma warning disable 1998

namespace LeafDNS.DnsSvr
{
    static class QueryResolve
    {
        public static List<DomainName> BlackList;
        public static Dictionary<DomainName, IPAddress> WhiteList;

        public static async Task ServerOnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            if (!(e.Query is DnsMessage query))
                return;

            IPAddress clientAddress = e.RemoteEndpoint.Address;
            if (DnsSettings.EDnsCustomize)
                clientAddress = Equals(DnsSettings.EDnsIp, IPAddress.Parse("0.0.0.1")) 
                    ? IPAddress.Parse(MainWindow.IntIPAddr.ToString().Substring(
                        0, MainWindow.IntIPAddr.ToString().LastIndexOf(".", StringComparison.Ordinal)) +".0") : DnsSettings.EDnsIp;
            else if (Equals(clientAddress, IPAddress.Loopback) || IpTools.InSameLaNet(clientAddress, MainWindow.LocIPAddr))
                clientAddress = MainWindow.IntIPAddr;

            DnsMessage response = query.CreateResponseInstance();

            if (query.Questions.Count <= 0)
                response.ReturnCode = ReturnCode.ServerFailure;

            else
            {
                foreach (DnsQuestion dnsQuestion in query.Questions)
                {
                    response.ReturnCode = ReturnCode.NoError;

                    if (DnsSettings.DebugLog)
                        BgwLog($@"| {DateTime.Now} {e.RemoteEndpoint.Address} : {dnsQuestion.Name} | {dnsQuestion.RecordType.ToString().ToUpper()}");

                    if (DnsSettings.BlackListEnable && BlackList.Contains(dnsQuestion.Name) && dnsQuestion.RecordType == RecordType.A)
                    {
                        //BlackList
                        ARecord blackRecord = new ARecord(dnsQuestion.Name, 10, IPAddress.Any);
                        response.AnswerRecords.Add(blackRecord);
                        if (DnsSettings.DebugLog)
                            BgwLog(@"|- BlackList");
                    }
                    else if (DnsSettings.WhiteListEnable && WhiteList.ContainsKey(dnsQuestion.Name) && dnsQuestion.RecordType == RecordType.A)
                    {
                        //WhiteList
                        ARecord blackRecord = new ARecord(dnsQuestion.Name, 10, WhiteList[dnsQuestion.Name]);
                        response.AnswerRecords.Add(blackRecord);
                        if (DnsSettings.DebugLog)
                            BgwLog(@"|- WhiteList");
                    }
                    else
                    {
                        //Resolve
                        try
                        {
                            var (resolvedDnsList, statusCode) = ResolveOverHttps(clientAddress.ToString(),
                                dnsQuestion.Name.ToString(), DnsSettings.HttpsDnsUrl, DnsSettings.ProxyEnable,
                                DnsSettings.WProxy, dnsQuestion.RecordType);
                            if (resolvedDnsList != null && resolvedDnsList != new List<dynamic>() && statusCode == ReturnCode.NoError)
                            {
                                foreach (var item in resolvedDnsList)
                                    response.AnswerRecords.Add(item);
                            }
                            else if (statusCode == ReturnCode.ServerFailure)
                            {
                                response.AnswerRecords = new DnsClient(DnsSettings.SecondDnsIp, 1000)
                                    .Resolve(dnsQuestion.Name, dnsQuestion.RecordType).AnswerRecords;
                                BgwLog($"| -- SecondDns : {DnsSettings.SecondDnsIp}");
                            }
                            else
                                response.ReturnCode = statusCode;
                        }
                        catch (Exception ex)
                        {
                            response.ReturnCode = ReturnCode.ServerFailure;
                            BgwLog(@"| " + ex);
                        }
                    }

                }
            }

            e.Response = response;

        }

        private static (List<dynamic> list, ReturnCode statusCode) ResolveOverHttps(string clientIpAddress, string domainName, string dohUrl,
            bool proxyEnable = false, IWebProxy wProxy = null, RecordType type = RecordType.A)
        {
            string dnsStr;
            List<dynamic> recordList = new List<dynamic>();

            using (MWebClient webClient = new MWebClient())
            {
                webClient.Headers["User-Agent"] = "AuroraDNSC/0.1";

//                webClient.AllowAutoRedirect = false;

                if (proxyEnable)
                    webClient.Proxy = wProxy;

                try
                {
                    dnsStr = webClient.DownloadString(dohUrl + @"?ct=application/dns-json&" +
                            $"name={domainName}&type={type.ToString().ToUpper()}&edns_client_subnet={clientIpAddress}");
                }
                catch (WebException e)
                {
                    HttpWebResponse response = (HttpWebResponse)e.Response;
                    try
                    {
                        BgwLog($@"| - Catch WebException : {Convert.ToInt32(response.StatusCode)} {response.StatusCode} | {domainName} | {dohUrl}");
                    }
                    catch (Exception exception)
                    {
                        BgwLog($@"| - Catch WebException : {exception.Message} | {domainName} | {dohUrl}");

                        //MainWindow.NotifyIcon.ShowBalloonTip(360, "AuroraDNS - 错误",
                        //    $"异常 : {exception.Message} {Environment.NewLine} {domainName}", ToolTipIcon.Warning);
                    }

                    if (dohUrl == DnsSettings.HttpsDnsUrl)
                    {
                        BgwLog($@"| -- SecondDoH : {DnsSettings.SecondHttpsDnsUrl}");
                        return ResolveOverHttps(clientIpAddress, domainName, DnsSettings.SecondHttpsDnsUrl,
                            proxyEnable, wProxy, type);
                    }

                    return (new List<dynamic>(), ReturnCode.ServerFailure);
                }
            }

            JsonValue dnsJsonValue = Json.Parse(dnsStr);

            int statusCode = dnsJsonValue.AsObjectGetInt("Status");
            if (statusCode != 0)
                return (new List<dynamic>(), (ReturnCode) statusCode);

            if (dnsStr.Contains("\"Answer\""))
            {
                var dnsAnswerJsonList = dnsJsonValue.AsObjectGetArray("Answer");

                foreach (var itemJsonValue in dnsAnswerJsonList)
                {
                    string answerAddr = itemJsonValue.AsObjectGetString("data");
                    string answerDomainName = itemJsonValue.AsObjectGetString("name");
                    int answerType = itemJsonValue.AsObjectGetInt("type");
                    int ttl = itemJsonValue.AsObjectGetInt("TTL");

                    switch (type)
                    {
                        case RecordType.A:
                        {
                            if (Convert.ToInt32(RecordType.A) == answerType)
                            {
                                ARecord aRecord = new ARecord(
                                    DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));

                                recordList.Add(aRecord);
                            }
                            else if (Convert.ToInt32(RecordType.CName) == answerType)
                            {
                                CNameRecord cRecord = new CNameRecord(
                                    DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));

                                recordList.Add(cRecord);

                                //recordList.AddRange(ResolveOverHttps(clientIpAddress,answerAddr));
                                //return recordList;
                            }

                            break;
                        }

                        case RecordType.Aaaa:
                        {
                            if (Convert.ToInt32(RecordType.Aaaa) == answerType)
                            {
                                AaaaRecord aaaaRecord = new AaaaRecord(
                                    DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));
                                recordList.Add(aaaaRecord);
                            }
                            else if (Convert.ToInt32(RecordType.CName) == answerType)
                            {
                                CNameRecord cRecord = new CNameRecord(
                                    DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                                recordList.Add(cRecord);
                            }
                            break;
                        }

                        case RecordType.CName when answerType == Convert.ToInt32(RecordType.CName):
                        {
                            CNameRecord cRecord = new CNameRecord(
                                DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                            recordList.Add(cRecord);
                            break;
                        }

                        case RecordType.Ns when answerType == Convert.ToInt32(RecordType.Ns):
                        {
                            NsRecord nsRecord = new NsRecord(
                                DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                            recordList.Add(nsRecord);
                            break;
                        }

                        case RecordType.Mx when answerType == Convert.ToInt32(RecordType.Mx):
                        {
                            MxRecord mxRecord = new MxRecord(
                                DomainName.Parse(answerDomainName), ttl,
                                ushort.Parse(answerAddr.Split(' ')[0]),
                                DomainName.Parse(answerAddr.Split(' ')[1]));
                            recordList.Add(mxRecord);
                            break;
                        }

                        case RecordType.Txt when answerType == Convert.ToInt32(RecordType.Txt):
                        {
                            TxtRecord txtRecord = new TxtRecord(DomainName.Parse(answerDomainName), ttl, answerAddr);
                            recordList.Add(txtRecord);
                            break;
                        }

                        case RecordType.Ptr when answerType == Convert.ToInt32(RecordType.Ptr):
                        {
                            PtrRecord ptrRecord = new PtrRecord(
                                DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                            recordList.Add(ptrRecord);
                            break;
                        }

                        default:
                        {
                            statusCode = Convert.ToInt32(ReturnCode.ServerFailure);
                            break;
                        }
                    }
                }
            }

            return (recordList, (ReturnCode) statusCode);
        }
    }
}
