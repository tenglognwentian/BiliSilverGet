﻿'b站挂瓜子的脚本而已

'Author: Beining --<i@cnbeining.com>
'Co-op: SuperFashi
'Purpose: Auto grab silver of Bilibili
'Created: 10/22/2015
'Last modified: 12/8/2015
' https://www.cnbeining.com/
' https://github.com/cnbeining

'source code : python ->  vb .net
'translator: pandasxd (4/3/2016)

Imports System.Threading
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.IO
Imports VBUtil.Utils.NetUtils
Imports VBUtil.Utils
Imports VBUtil
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports System.Net
Imports System.Net.Sockets

''' <summary>
''' 哼，我就要瓜子，你来咬我啊
''' </summary>
''' <remarks></remarks>

Public Module Variables
End Module
Public Class guazi
    Public Event DebugOutput(ByVal msg As String)
    Public Event FinishedGrabbing()

    Private _workThd As Thread
    Private _startTime As Integer
    Private _RoomId As Integer
    Private _RoomURL As Integer
    Private _RoomInfo As JObject

    Private Const APPKEY As String = "85eb6835b0a1034e"
    Private Const SECRETKEY As String = "2ad42749773c441109bdc0191257a664"
    Private Const VER As String = "0.98.86"

    Private Const DEBUG_RETURN_INFO As Boolean = False

    'grabbing silver module
    Private Function calc_sign(ByVal str As String) As String
        Dim md5 As New System.Security.Cryptography.MD5CryptoServiceProvider

        Return Utils.Others.Hex(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(str))).ToLower
    End Function

    Private _expireTime As Date
    Public Event RefreshClock(ByVal expireTime As Date, ByVal silver As Integer)
    ''' <summary>
    ''' 领取瓜子线程回调函数
    ''' </summary>
    ''' <remarks></remarks>
    Private Sub GuaziCallBack()
        Try
            _expireTime = Date.MinValue

            Dim je As JObject = Nothing
            '循环领取瓜子,直到领取结束
            Do
                Dim silver As Integer = 0

                '获取新的任务
                Do
                    Try
                        je = get_new_task_time_and_award()
                    Catch ex As Exception
                        RaiseEvent DebugOutput(ex.ToString)
                        Debug.Print(ex.ToString)
                    End Try
                    Dim code As Integer = je.Value(Of Integer)("code")
                    If code = -10017 Then
                        RaiseEvent DebugOutput("本日瓜子已领完，欢迎下次再来XD")
                        RaiseEvent FinishedGrabbing()
                        Exit Try
                    End If
                Loop While je Is Nothing OrElse je.Value(Of Integer)("code") <> 0
                '计算时间
                Dim minutes As Integer = je("data").Value(Of Integer)("minute")
                silver = je("data").Value(Of Integer)("silver")
                _expireTime = Now.AddMinutes(minutes)
                RaiseEvent RefreshClock(_expireTime, silver)

                '倒计+发送心跳
                Dim request_ms As Integer = 0
                Dim sw As New Stopwatch
                '结束倒计的标识改为在心跳接收到"isAward":true时退出
                Dim loop_end_flag As Boolean = False
                Do
                    Dim sleep_time As Integer = 60000 - request_ms
                    Thread.Sleep(sleep_time)

                    Try
                        sw.Start()
                        Dim json As JObject = send_heartbeat()
                        loop_end_flag = CType(json("data"), JObject).Value(Of Boolean)("isAward")
                    Catch ex As Exception
                        RaiseEvent DebugOutput(ex.ToString)
                        Debug.Print(ex.ToString)
                    Finally
                        sw.Stop()
                        request_ms = sw.ElapsedMilliseconds
                        sw.Reset()
                    End Try
                Loop Until loop_end_flag

                '领取前的状态检查
                Dim plus_time As Integer = 0
                request_ms = 0
                Do
                    Dim sleep_time As Integer = 60000 * plus_time - request_ms
                    If sleep_time > 0 Then Thread.Sleep(sleep_time)
                    Try
                        sw.Start()
                        plus_time = award_requests()
                    Catch ex As Exception
                        RaiseEvent DebugOutput(ex.ToString)
                        Debug.Print(ex.ToString)
                    Finally
                        sw.Stop()
                        request_ms = sw.ElapsedMilliseconds
                        sw.Reset()
                    End Try
                Loop While plus_time

                '领取瓜子
                Dim getsilver, total_silver As Integer
                Try
                    je = get_award()
                    If je.Value(Of Integer)("code") = 0 Then

                        getsilver = je("data").Value(Of Integer)("awardSilver")
                        total_silver = je("data").Value(Of Integer)("silver")

                        If getsilver > 0 Then
                            RaiseEvent DebugOutput("领取成功！得到" & getsilver & "个银瓜子(总" & total_silver & "个)")
                            _expireTime = Date.MinValue
                        End If
                    Else
                        RaiseEvent DebugOutput("领取错误")
                        _expireTime = Date.MinValue
                    End If
                Catch ex As Exception
                    RaiseEvent DebugOutput(ex.ToString)
                    Debug.Print(ex.ToString)
                End Try

            Loop
        Catch ex As Exception
            RaiseEvent DebugOutput("[ERR] 抛出异常: " & vbCrLf & ex.ToString)
        End Try
    End Sub

    ''' <summary>
    ''' 获取房间的信息，用于投票
    ''' </summary>
    ''' <remarks></remarks>
    Private Sub get_room_info()
        If _RoomURL <= 0 Then
            _RoomInfo = New JObject
            Return
        End If
        'room url -> room id
        Dim room_url As String = "http://live.bilibili.com/" & _RoomURL
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000
        req.HttpGet(room_url)
        Dim str As String = req.ReadResponseString
        req.Close()

        Dim reg As Match = Regex.Match(str, "var\s+ROOMID\s+=\s+(\d+);")
        If reg.Success = False Then Throw New ArgumentException("Can not get RoomID")
        _RoomId = Integer.Parse(reg.Result("$1"))

        Dim info_url As String = "http://live.bilibili.com/live/getInfo"
        Dim param As New Parameters
        param.Add("roomid", _RoomId)
        req.HttpGet(info_url, , param)

        str = req.ReadResponseString

        Debug.Print("Get room #" & _RoomURL & "(" & _RoomId & ") info succeeded, response returned value:")
        Debug.Print(str)

        req.Close()

        _RoomInfo = JsonConvert.DeserializeObject(str)

        _startTime = Int(Utils.Others.ToUnixTimestamp(Now))

    End Sub

    ''' <summary>
    ''' 获取新的任务
    ''' </summary>
    ''' <returns>请求后返回的JSON对象</returns>
    ''' <remarks></remarks>
    Private Function get_new_task_time_and_award() As JObject
        Dim url As String = "http://live.bilibili.com/mobile/freeSilverCurrentTask"


        Dim param As New Parameters
        param.Add("appkey", APPKEY)
        param.Add("platform", "ios")

        'sign calc
        param.Add("sign", calc_sign(param.BuildURLQuery & SECRETKEY))

        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000
        req.HttpGet(url, , param)
        Dim str As String = ReadToEnd(req.Stream)

        Debug.Print("Get new tasks succeeded, response returned value:")
        Debug.Print(str)

        req.Close()

        Dim ret As JObject = JsonConvert.DeserializeObject(str)

        Return ret
    End Function

    ''' <summary>
    ''' 发送心跳包
    ''' </summary>
    ''' <returns>心跳包返回的状态码</returns>
    ''' <remarks></remarks>
    Private Function send_heartbeat() As JObject
        Dim url As String = "http://live.bilibili.com/mobile/freeSilverHeart"
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000
        Dim param As New Parameters

        param.Add("appkey", APPKEY)
        param.Add("platform", "ios")
        param.Add("sign", calc_sign(param.BuildURLQuery & SECRETKEY))

        req.HttpGet(url, , param)
        Dim str As String = ReadToEnd(req.Stream)

        Debug.Print("Send Heartbeat succeeded, response returned value:")
        Debug.Print(str)

        Dim a As JObject = JsonConvert.DeserializeObject(str)
        Dim statuscode As HttpStatusCode = req.HTTP_Response.StatusCode
        req.Close()

        Return a
    End Function

    ''' <summary>
    ''' 领取瓜子
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function get_award() As JObject
        Dim url As String = "http://live.bilibili.com/mobile/freeSilverAward"
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000
        Dim param As New Parameters
        param.Add("appkey", APPKEY)
        param.Add("platform", "ios")

        param.Add("sign", calc_sign(param.BuildURLQuery & SECRETKEY))

        req.HttpGet(url, , param)
        Dim str As String = ReadToEnd(req.Stream)

        Debug.Print("Get award succeeded, response returned value:")
        Debug.Print(str)


        Dim a As JObject = JsonConvert.DeserializeObject(str)
        Dim statuscode As HttpStatusCode = req.HTTP_Response.StatusCode
        req.Close()

        If a.Value(Of Integer)("code") <> 0 Then
            RaiseEvent DebugOutput(a.Value(Of String)("message"))
        End If
        Return a
    End Function

    ''' <summary>
    ''' 领取瓜子前的请求(前戏？)
    ''' </summary>
    ''' <returns>额外的分钟：>=0</returns>
    ''' <remarks></remarks>
    Private Function award_requests() As Integer
        Dim url As String = "http://live.bilibili.com/mobile/freeSilverSurplus"
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000
        Dim param As New Parameters
        param.Add("appkey", APPKEY)
        param.Add("platform", "ios")
        param.Add("sign", calc_sign(param.BuildURLQuery & SECRETKEY))
        req.HttpGet(url, , param)
        Dim str As String = ReadToEnd(req.Stream)

        Debug.Print("Get award request succeeded, response returned value:")
        Debug.Print(str)

        Dim a As JObject = JsonConvert.DeserializeObject(str)
        Dim statuscode As HttpStatusCode = req.HTTP_Response.StatusCode
        req.Close()

        Return a("data").Value(Of Integer)("surplus")
    End Function

    ''' <summary>
    ''' 每日签到函数
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function daily_sign() As JObject
        Dim url As String = "http://live.bilibili.com/sign/doSign"
        Dim status_url As String = "http://live.bilibili.com/sign/GetSignInfo"

        Dim http_req As New NetStream
        http_req.ReadWriteTimeout = 5000
        http_req.Timeout = 5000
        http_req.HttpGet(status_url)
        Dim rep As String = ReadToEnd(http_req.Stream)
        http_req.Close()

        Debug.Print("Daily sign succeeded, response returned value:")
        Debug.Print(rep)

        Dim ret As JObject = JsonConvert.DeserializeObject(rep)
        Dim sign_status As Integer = ret("data").Value(Of Integer)("status")

        If sign_status = 0 Then
            http_req.HttpGet(url)
            rep = ReadToEnd(http_req.Stream)
            http_req.Close()
            Return JsonConvert.DeserializeObject(rep)
        End If

        Return ret
    End Function

    ''' <summary>
    ''' 获得周期性赠送的道具
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Private Function get_send_gift() As JObject
        Dim url As String = "http://live.bilibili.com/giftBag/sendDaily"
        'Dim url2 As String = "http://live.bilibili.com/giftBag/getSendGift"
        Dim http_req As New NetStream
        http_req.ReadWriteTimeout = 5000
        http_req.Timeout = 5000
        http_req.HttpGet(url)
        'http_req.HttpGet(url2)
        Dim rep As String = ReadToEnd(http_req.Stream)
        http_req.Close()

        Debug.Print("Get send gift succeeded, response returned value:")
        Debug.Print(rep)

        Return JsonConvert.DeserializeObject(rep)
    End Function

    ''' <summary>
    ''' 获取用户道具列表，并确定是否自动送出
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function SyncGetPlayerBag(Optional ByVal auto_send As Boolean = False) As JObject
        Dim url As String = "http://live.bilibili.com/gift/playerBag"
        Dim http_req As New NetStream
        http_req.ReadWriteTimeout = 5000
        http_req.Timeout = 5000
        http_req.HttpGet(url)
        Dim rep As String = ReadToEnd(http_req.Stream)

        Debug.Print("Get player bag succeeded, response returned value:")
        Debug.Print(rep)

        http_req.Close()

        Dim ret As JObject = JsonConvert.DeserializeObject(rep)

        If auto_send AndAlso _RoomId > 0 Then
            Dim arr As JArray = ret.Value(Of JArray)("data")
            '获取道具名称
            For Each item As JObject In arr
                Dim gift_id As Integer = item.Value(Of Integer)("gift_id")
                Dim gift_num As Integer = item.Value(Of Integer)("gift_num")
                Dim id As Integer = item.Value(Of Integer)("id")

                SyncSendGift(gift_id, id, gift_num)
            Next

            '重置返回值
            If arr.Count Then
                http_req.HttpGet(url)
                rep = ReadToEnd(http_req.Stream)
                http_req.Close()
                ret = JsonConvert.DeserializeObject(rep)
            End If

            Return ret
        Else
            Return ret
        End If
    End Function

    Public Function SyncSendGift(ByVal giftid As Integer, ByVal bagid As Integer, ByVal giftnum As Integer) As Boolean
        If _RoomId <= 0 Then Return False
        Try

            Dim http_req As New NetStream
            http_req.ReadWriteTimeout = 5000
            http_req.Timeout = 5000
            'HTTP请求
            Dim req_param As New Parameters
            Dim gift_send_url As String = "http://live.bilibili.com/giftBag/send"
            req_param.Add("giftId", giftid)
            req_param.Add("roomid", _RoomId)
            req_param.Add("ruid", _RoomInfo("data").Value(Of Integer)("MASTERID"))
            req_param.Add("num", giftnum)
            req_param.Add("coinType", "silver")
            req_param.Add("Bag_id", bagid)
            req_param.Add("timestamp", CInt(VBUtil.Utils.Others.ToUnixTimestamp(Now)))
            req_param.Add("rnd", VBUtil.Utils.Others.rand.Next())
            req_param.Add("token", DefaultCookieContainer.GetCookies(New Uri(gift_send_url))("LIVE_LOGIN_DATA").Value)

            http_req.HttpPost(gift_send_url, req_param)

            Dim post_result As String = http_req.ReadResponseString
            http_req.Close()

            Dim post_result_ds As JObject = JsonConvert.DeserializeObject(post_result)
            Dim post_result_code As Integer = post_result_ds.Value(Of Integer)("code")
            If post_result_code = 0 Then
                RaiseEvent DebugOutput("送出道具成功(道具编号:" & giftid & ",数量:" & giftnum & ")")
            Else
                RaiseEvent DebugOutput("送出道具失败，返回数据:" & vbCrLf & post_result_ds.ToString)
            End If
            Return True
        Catch ex As Exception
            RaiseEvent DebugOutput("[ERR]" & ex.ToString)
            Return False
        End Try
    End Function
    'public functions
    ''' <summary>
    ''' 构造函数，roomid之前是用来送投票券和道具的
    ''' </summary>
    ''' <param name="roomid"></param>
    ''' <remarks></remarks>
    Public Sub New(Optional ByVal roomid As Integer = 0)
        _RoomURL = roomid
        _RoomInfo = Nothing
        _DownloadManager = New HTTP_Stream_Manager
        _expireTime = Date.MinValue
        Try
            get_room_info()
        Catch ex As Exception
            Throw ex
        End Try
    End Sub

    '开始领取瓜子
    Public Sub AsyncStartGrabbingSilver()
        If _workThd Is Nothing OrElse (_workThd.ThreadState = ThreadState.Stopped Or _workThd.ThreadState = ThreadState.Aborted) Then
            _workThd = New Thread(AddressOf GuaziCallBack)
            _workThd.Name = "Bili Live Auto Grabbing Silver Thread"
        End If

        If _workThd.ThreadState = ThreadState.Unstarted Then
            _workThd.Start()
        End If
    End Sub
    '停止领取瓜子
    Public Sub AsyncEndGrabbingSilver()
        If _workThd.ThreadState = ThreadState.Running Then
            _workThd.Abort()
        End If
    End Sub
    '获得每日道具
    Public Sub AsyncGetDailyGift()
        Dim thd As New Thread(
            Sub()
                Try
                    '领取道具
                    Dim gift_rep As JObject = get_send_gift()
                    If gift_rep.Value(Of Integer)("code") = 0 Then
                        RaiseEvent DebugOutput("领取每日道具成功")
                        RaiseEvent GetDailyGiftFinished()
                    Else
                        RaiseEvent DebugOutput("领取道具失败，返回数据:" & vbCrLf & gift_rep.ToString)
                    End If
                Catch ex As Exception
                    RaiseEvent DebugOutput("[ERR] 抛出异常: " & ex.ToString)
                End Try
            End Sub)

        thd.Name = "Get Daily Gift Thread"
        thd.Start()
    End Sub
    Public Event GetDailyGiftFinished()
    '赠送每日道具
    Public Sub AsyncSendDailyGift()
        If _RoomId <= 0 Then Return
        Dim thd As New Thread(
            Sub()
                Try
                    SyncGetPlayerBag(True)
                    RaiseEvent SendDailyGiftFinished()
                Catch ex As Exception
                    RaiseEvent DebugOutput("[ERR] 抛出异常: " & ex.ToString)
                End Try
            End Sub)

        thd.Name = "Send Daily Gift Thread"
        thd.Start()
    End Sub
    Public Event SendDailyGiftFinished()
    '签到
    Public Sub AsyncDoSign()
        Dim thd As New Thread(
            Sub()
                Try

                    '签到
                    Dim dosign As JObject = daily_sign()
                    Dim sign_state As Integer = dosign.Value(Of Integer)("code")

                    Select Case sign_state
                        Case 0
                            RaiseEvent DebugOutput("已完成签到")
                            RaiseEvent DoSignSucceeded()
                        Case Else
                            RaiseEvent DebugOutput("未知错误:[" & sign_state & "]" & dosign.Value(Of String)("msg"))
                    End Select

                Catch ex As Exception
                    RaiseEvent DebugOutput("ERR] 抛出异常: " & ex.ToString)
                End Try
            End Sub)

        thd.Name = "Daily Sign Thread"
        thd.Start()
    End Sub
    Public Event DoSignSucceeded()
    Public Property RoomURL() As Integer
        Get
            Return _RoomURL
        End Get
        Set(value As Integer)
            If _RoomURL = value Then Return
            RaiseEvent DebugOutput("进入房间:" & value & "成功")
            _RoomURL = value
            get_room_info()
            AsyncStopDownloadStream()
            Dim recv As Boolean = _isReceivingComment
            AsyncStopReceiveComment()
            Threading.ThreadPool.QueueUserWorkItem(
                Sub()
                    If recv Then
                        _CommentThd.Join()
                        AsyncStartReceiveComment()
                    End If
                End Sub)
        End Set
    End Property
    Public ReadOnly Property RoomID() As Integer
        Get
            Return _RoomId
        End Get
    End Property

    '录播
    Private Const DEFAULT_VIDEO_URL As String = "http://live.bilibili.com/api/playurl"
    Private WithEvents _DownloadManager As HTTP_Stream_Manager
    Public Event DownloadStarted()
    Public Event DownloadStopped()
    Private Sub OnDownloadStatusChange(ByVal name As String, ByVal fromstatus As VBUtil.HTTP_Stream_Manager.StreamStatus, ByVal tostatus As VBUtil.HTTP_Stream_Manager.StreamStatus) Handles _DownloadManager.StatusUpdate
        If tostatus = HTTP_Stream_Manager.StreamStatus.STATUS_WORK Then
            RaiseEvent DownloadStarted()
        ElseIf tostatus = HTTP_Stream_Manager.StreamStatus.STATUS_STOP Then
            RaiseEvent DownloadStopped()
        End If
    End Sub
    Public Event DownloadSpeed(ByVal speed As Integer)
    Private Sub OnSpeedChange() Handles _DownloadManager.SpeedUpdate
        RaiseEvent DownloadSpeed(_DownloadManager.GetSpeed())
    End Sub
    Public Sub AsyncStartDownloadStream(ByVal path As String)
        If _RoomId <= 0 Then Return

        Try

            Dim param As New Parameters
            param.Add("cid", _RoomId)
            param.Add("player", 1)
            param.Add("quality", 0)

            Dim xml_document As New Xml.XmlDocument
            Dim req As New NetStream
            req.ReadWriteTimeout = 5000
            req.Timeout = 5000
            req.HttpGet(DEFAULT_VIDEO_URL, , param)
            Dim xml_str As String = ReadToEnd(req.Stream)
            req.Close()
            xml_document.LoadXml(xml_str)

            Dim url As String = xml_document("video")("durl")("url").InnerText

            _DownloadManager.AddDownloadTaskAndStart(path, url, "Video")

        Catch ex As Exception
            RaiseEvent DebugOutput("[ERR] 抛出异常: " & ex.ToString)
            RaiseEvent DownloadStopped()
        End Try
    End Sub

    Public Sub AsyncStopDownloadStream()
        Try
            _DownloadManager.StopTask("Video")
        Catch ex As Exception
            RaiseEvent DebugOutput("[ERR] 抛出异常: " & ex.ToString)

        End Try
    End Sub


    '弹幕
    Private _CommentThd As Thread
    Private _CommentHeartBeat As Thread
    Private Const DEFAULT_COMMENT_HOST As String = "livecmt-1.bilibili.com"
    Private Const DEFAULT_COMMENT_PORT As Integer = 788
    Private _CommentSocket As Socket
    Private _isReceivingComment As Boolean
    Private Sub CommentHeartBeatCallBack()
        Dim next_update_time As Date = Now.AddSeconds(10)

        Do
            Dim ts As TimeSpan = next_update_time - Now
            If ts.TotalMilliseconds > 0 Then
                Thread.Sleep(ts)
                next_update_time = next_update_time.AddSeconds(10)
            End If

            If _CommentSocket IsNot Nothing AndAlso _CommentSocket.Connected = True Then
                Try
                    SendSocketHeartBeat()
                Catch ex As Exception
                    Debug.Print(ex.ToString)
                    RaiseEvent DebugOutput("[ERR]" & ex.ToString)
                End Try
            End If
        Loop
    End Sub
    Private Sub CommentThdCallback()
        If _RoomId <= 0 Then Return
        Dim ipaddr As IPAddress = Dns.GetHostAddresses(DEFAULT_COMMENT_HOST)(0)
        Dim ip_ed As IPEndPoint = New IPEndPoint(ipaddr, DEFAULT_COMMENT_PORT)

        _CommentSocket = New Sockets.Socket(Sockets.AddressFamily.InterNetwork, Sockets.SocketType.Stream, Sockets.ProtocolType.Tcp)
        Dim buffer(1048575) As Byte
        Dim length As Integer = 0
        Try
            _CommentSocket.Connect(ip_ed)

            Debug.Print("Comment Socket: Sending User data")
            RaiseEvent DebugOutput("开始接收 " & _RoomURL & " 房间的弹幕信息")

            Dim param As New JObject
            param.Add("roomid", _RoomId)
            param.Add("uid", 100000000000000 + CLng((200000000000000 * Utils.rand.NextDouble())))


            SendSocketData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(param)))
            SendSocketHeartBeat()
            Do
                length = _CommentSocket.Receive(buffer)
                Debug.Print("Comment Socket: Received a socket data: length = " & length & "Byte")
                If length <> 0 Then
                    Try
                        ParseSocketData(buffer, length)
                    Catch ex As Exception
                        RaiseEvent DebugOutput("[ERR]" & ex.ToString)
                        Debug.Print(ex.ToString)
                    End Try
                End If

            Loop

        Catch ex2 As ThreadAbortException

        Catch ex As Exception
            RaiseEvent DebugOutput("[ERR]" & ex.ToString)
        Finally
            _CommentSocket.Disconnect(True)
            _CommentSocket = Nothing
        End Try
    End Sub
    Private Sub SendSocketData(ByVal data() As Byte)
        '套接字 v1.0
        Dim data_length As UInteger = 0
        If data IsNot Nothing Then data_length = data.Length
        Dim total_len As UInteger = 16 + data_length
        Dim head_len As UShort = 16
        Dim version As UShort = 1
        Dim request_type As UInteger = 7
        Dim param5 As UInteger = 1

        Dim buf As New MemoryStream
        WriteUI32(buf, total_len)
        WriteUI16(buf, head_len)
        WriteUI16(buf, version)
        WriteUI32(buf, request_type)
        WriteUI32(buf, param5)
        If data_length > 0 Then buf.Write(data, 0, data_length)
        buf.Position = 0
        Dim post_data(total_len - 1) As Byte
        buf.Read(post_data, 0, total_len)
        If _CommentSocket IsNot Nothing Then
            _CommentSocket.Send(post_data)
        End If
        buf.Close()
    End Sub
    Private Sub SendSocketHeartBeat()

        Debug.Print("Comment Socket: Sending Heartbeat data")

        Dim total_len As UInteger = 16
        Dim head_len As UShort = 16
        Dim version As UShort = 1
        Dim request_type As UInteger = 2
        Dim param5 As UInteger = 1

        Dim buf As New MemoryStream
        WriteUI32(buf, total_len)
        WriteUI16(buf, head_len)
        WriteUI16(buf, version)
        WriteUI32(buf, request_type)
        WriteUI32(buf, param5)

        buf.Position = 0
        Dim post_data(15) As Byte
        buf.Read(post_data, 0, 16)
        If _CommentSocket IsNot Nothing Then
            _CommentSocket.Send(post_data)
        End If
        buf.Close()

    End Sub
    Public Event ReceivedOnlinePeople(ByVal people As Integer)
    Public Event ReceivedComment(ByVal unixTimestamp As Long, ByVal username As String, ByVal msg As String)
    Public Event ReceivedGiftSent(ByVal unixTimestamp As Long, ByVal giftName As String, ByVal giftId As Integer, ByVal giftNum As Integer, ByVal user As String)
    Public Event ReceivedWelcome(ByVal admin As Boolean, ByVal vip As Boolean, ByVal name As String)
    Public Event ReceivedSystemMsg(ByVal msg As String, ByVal refer_url As String)
    Private Sub ParseSocketData(ByVal data() As Byte, ByVal length As Integer)
        'todo: 兼容两条receive后合并处理的数据

        '3: online people
        '5: msg
        Dim ms As New MemoryStream
        ms.Write(data, 0, length)
        ms.Position = 0

        While ms.Position < ms.Length
            Dim total_len As UInteger = ReadUI32(ms)
            Dim head_len As UShort = ReadUI16(ms)
            If head_len = 0 Then
                ms.Close()
                Return
            End If
            Dim version As UShort = ReadUI16(ms)
            Dim type As UInteger = ReadUI32(ms)
            Dim param5 As UInteger = ReadUI32(ms)

            Select Case type
                Case 3
                    RaiseEvent ReceivedOnlinePeople(ReadUI32(ms))

                Case 5
                    Dim buf(total_len - head_len - 1) As Byte
                    ms.Read(buf, 0, total_len - head_len)
                    Dim str As String = Encoding.UTF8.GetString(buf)

                    Debug.Print("Parsing socket data:" & str)
                    Dim str_obj As JObject = Nothing
                    Try
                        str_obj = JsonConvert.DeserializeObject(str)
                    Catch ex As Exception
                        Debug.Print("An error occured while deserializing json data:")
                        Debug.Print("[TRACE] Origin String: " & str)
                        Debug.Print("[TRACE] Exception Type: " & ex.ToString)
                        Continue While
                    End Try

                    Select Case str_obj.Value(Of String)("cmd")

                        Case "DANMU_MSG"
#Region "Example and remark"
                            '{"info":[[0,1,25,16777215,1469456771,907357409,0,"4d505c40",0],"摸摸阿一",[20940214,"半枫そう",0,0,0,10000],[4,"无常","小十里",22714,6606973],[20,226959,6215679],["ice-dust"]],"cmd":"DANMU_MSG"}

                            '[
                            '   [
                            '       弹幕开始时间(默认为0),
                            '       弹幕类型(默认为1 - 滚动弹幕),
                            '       弹幕大小(px)(默认为25),
                            '       弹幕颜色(rrggbb),
                            '       弹幕发送时间戳(unix),
                            '       (?) ,
                            '       弹幕池类型(默认为0 - 普通弹幕池),
                            '       用户代号,
                            '       (?) - (默认为0)
                            '   ],
                            '   弹幕信息,
                            '   [
                            '       用户id,
                            '       用户名称,
                            '       (?) ,
                            '       是否老爷,
                            '       (?) ,
                            '       用户权限(默认为10000)
                            '   ], [ （此处可无）
                            '       勋章等级,
                            '       勋章名称,
                            '       勋章博主名称,
                            '       勋章房间id,
                            '       (?)
                            '   ], [
                            '       用户等级,
                            '       用户排名,
                            '       (?)
                            '   ], [ （此处可无）
                            '       用户头衔 -> "sign-one-month" : 月老; "ice-dust" : 钻石星尘
                            '   ]
                            ']
#End Region
                            '暂时先撸这么多参数吧

                            Dim color As UInteger = str_obj("info").Value(Of JArray)(0)(3)
                            Dim post_time As UInteger = str_obj("info").Value(Of JArray)(0)(4)
                            Dim msg As String = str_obj("info").Value(Of String)(1)
                            Dim user_name As String = str_obj("info").Value(Of JArray)(2)(1)
                            Dim user_hashid As String = str_obj("info").Value(Of JArray)(0)(7)
                            '勋章等级、名称以及来源up主
                            Dim medal_level As Integer
                            Dim medal_name As String
                            Dim medal_up_name As String

                            If str_obj("info").Value(Of JArray)(3).Count Then
                                medal_level = str_obj("info").Value(Of JArray)(3)(0)
                                medal_name = str_obj("info").Value(Of JArray)(3)(1)
                                medal_up_name = str_obj("info").Value(Of JArray)(3)(2)
                            End If

                            RaiseEvent ReceivedComment(post_time, user_name, msg)
                        Case "SEND_GIFT"
#Region "Example and remark"
                            '{"cmd":"SEND_GIFT","data":{"giftName":"\u8fa3\u6761","num":6,"uname":"\u841d\u00b7\u5bbe\u6c49","rcost":2793402,"uid":1974757,"top_list":[],"timestamp":1469457764,"giftId":1,"giftType":0,"action":"\u5582\u98df","super":0,"price":100,"rnd":"1469457648","newMedal":0,"medal":1,"capsule":[]},"roomid":22714}
                            ' no remark
#End Region

                            Dim gift_name As String = str_obj("data").Value(Of String)("giftName")
                            Dim gift_num As Integer = str_obj("data").Value(Of Integer)("num")
                            Dim user_name As String = str_obj("data").Value(Of String)("uname")
                            Dim rcost As Integer = str_obj("data").Value(Of Integer)("rcost")
                            Dim uid As Integer = str_obj("data").Value(Of Integer)("uid")
                            Dim top_list As JArray = str_obj("data").Value(Of JArray)("top_list")
                            Dim timestamp As Long = str_obj("data").Value(Of Long)("timestamp")
                            Dim gift_id As Integer = str_obj("data").Value(Of Integer)("giftId")
                            Dim gift_type As Integer = str_obj("data").Value(Of Integer)("giftType")
                            Dim action As String = str_obj("data").Value(Of String)("action")
                            Dim super As Integer = str_obj("data").Value(Of Integer)("super")
                            Dim price As Integer = str_obj("data").Value(Of Integer)("price")
                            Dim rnd As String = str_obj("data").Value(Of String)("rnd")
                            Dim new_medal As Integer = str_obj("data").Value(Of Integer)("newMedal")
                            'Dim medal As Integer = str_obj("data").Value(Of Integer)("medal")
                            Dim room_id As Integer = str_obj.Value(Of Integer)("roomid")

                            RaiseEvent ReceivedGiftSent(timestamp, gift_name, gift_id, gift_num, user_name)
                        Case "WELCOME"
#Region "Example and remark"
                            '{"cmd":"WELCOME","data":{"uid":6011599,"uname":"\u96c1\u675e\u5357\u98de","isadmin":0,"vip":1},"roomid":22714}
                            ' no remark
#End Region
                            Dim is_admin As Integer = str_obj("data").Value(Of Integer)("isadmin")
                            Dim is_vip As Integer = str_obj("data").Value(Of Integer)("vip")
                            Dim uid As Integer = str_obj("data").Value(Of Integer)("uid")
                            Dim user_name As String = str_obj("data").Value(Of String)("uname")
                            Dim room_id As Integer = str_obj.Value(Of Integer)("roomid")

                            RaiseEvent ReceivedWelcome(is_admin, is_vip, user_name)
                        Case "SYS_MSG"
#Region "Example and remark"
                            '{"cmd":"SYS_MSG","msg":"\u606d\u559c:?\u3010\u94f6\u5723\u7433\u3011:?\u5728\u76f4\u64ad\u95f4:?\u3010240\u3011:?\u62bd\u5230 \u5927\u53f7\u5c0f\u7535\u89c6\u62b1\u6795\u4e00\u4e2a","rep":1,"styleType":2,"url":""}
                            ' no remark
#End Region
                            Dim msg As String = str_obj.Value(Of String)("msg")
                            Dim url As String = str_obj.Value(Of String)("url")
                            RaiseEvent ReceivedSystemMsg(msg, url)
                        Case "SYS_GIFT"

#Region "Example and remark"
                            '{"cmd":"SYS_GIFT","msg":"\u6263\u5b50\u6316:? \u5728\u5e05\u70b8\u4e4c\u51ac\u7684:?\u76f4\u64ad\u95f4138:?\u5185\u8d60\u9001:?36:?\u5171100\u4e2a\uff0c\u89e6\u53d11\u6b21\u5228\u51b0\u96e8\u62bd\u5956\uff0c\u5feb\u53bb\u524d\u5f80\u62bd\u5
                            ' 可能是官方的漏洞？显示不全？
#End Region
                            Dim msg As String = str_obj.Value(Of String)("msg")
                            Dim url As String = str_obj.Value(Of String)("url")
                            RaiseEvent ReceivedSystemMsg(msg, url)

                        Case Else

                    End Select
            End Select
        End While

        ms.Close()
    End Sub
    Public Sub AsyncStartReceiveComment()
        If _RoomId <= 0 Then Return
        If _CommentThd Is Nothing OrElse (_CommentThd.ThreadState = ThreadState.Stopped Or _CommentThd.ThreadState = ThreadState.Aborted) Then

            _CommentThd = New Thread(AddressOf CommentThdCallback)
            _CommentThd.Name = "Bili Live Socket Thread"
            _CommentHeartBeat = New Thread(AddressOf CommentHeartBeatCallBack)
            _CommentHeartBeat.Name = "Bili Live Socket Heartbeat Thread"
        End If

        If _CommentThd.ThreadState = ThreadState.Unstarted Then
            _isReceivingComment = True
            _CommentThd.Start()
            _CommentHeartBeat.Start()
        End If
    End Sub
    Public Sub AsyncStopReceiveComment()
        If _CommentThd IsNot Nothing AndAlso _CommentThd.ThreadState = ThreadState.Running Then
            _isReceivingComment = False
            _CommentThd.Abort()
            _CommentHeartBeat.Abort()
        End If
    End Sub

    '挂机刷经验（爽爽爽 XD）
    Private _liveOnThd As Thread
    Private Sub liveOnThdCallback()
        Dim req As New NetStream
        Dim url As String = "http://live.bilibili.com/User/userOnlineHeart"
        Do
            Try
                Dim next_time As Date = Now.AddMinutes(5)
                RaiseEvent NextOnlineHeartBeatTime(next_time)

                req.HttpPost(url, New Byte() {}, "text/plain")

                Dim str As String = req.ReadResponseString
                Debug.Print("Sending Online Heartbeat succeeded, response returned value:")
                Debug.Print(str)

                req.Close()
                Dim sleep_time As TimeSpan = next_time - Now
                If sleep_time.TotalMilliseconds > 0 Then Thread.Sleep(sleep_time)
            Catch ex2 As ThreadAbortException
                Exit Do
            Catch ex As Exception
                Debug.Print(ex.ToString)
            End Try
        Loop
    End Sub
    Public Event NextOnlineHeartBeatTime(ByVal time As Date)
    Public Sub AsyncBeginLiveOn()
        If _liveOnThd Is Nothing OrElse (_liveOnThd.ThreadState = ThreadState.Stopped Or _liveOnThd.ThreadState = ThreadState.Aborted) Then
            _liveOnThd = New Thread(AddressOf liveOnThdCallback)
            _liveOnThd.Name = "Bili Live Online Thread"
        End If

        If _liveOnThd.ThreadState = ThreadState.Unstarted Then
            _liveOnThd.Start()
        End If
    End Sub
    Public Sub AsyncStopLiveOn()
        If _liveOnThd Is Nothing Then Return
        _liveOnThd.Abort()
    End Sub

    'b站限时活动 目前是领取什么扇的活动，所以在活动结束后不要调用 :-D
    Private _timeLimitEventThd As Thread
    Private Sub EventThdCallback()

        Dim req As New NetStream
        Dim url As String = "http://live.bilibili.com/summer/heart"
        Do
            Try
                Dim next_time As Date = Now.AddMinutes(5)
                RaiseEvent NextEventGrabTime(next_time)

                req.HttpPost(url, New Byte() {}, "text/html")
                Dim ret_str As String = req.ReadResponseString

                Debug.Print("Sending Special Event Heartbeat succeeded, response returned value:")
                Debug.Print(ret_str)

                req.Close()

                Dim sleep_time As TimeSpan = next_time - Now
                If sleep_time.TotalMilliseconds > 0 Then Thread.Sleep(sleep_time)
            Catch ex2 As ThreadAbortException
                Exit Do
            Catch ex As Exception
                Debug.Print(ex.ToString)
            End Try
        Loop
    End Sub
    Public Sub AsyncBeginTimeLimitedEvent()
        If _timeLimitEventThd Is Nothing OrElse (_timeLimitEventThd.ThreadState = ThreadState.Stopped Or _timeLimitEventThd.ThreadState = ThreadState.Aborted) Then
            _timeLimitEventThd = New Thread(AddressOf EventThdCallback)
            _timeLimitEventThd.Name = "Bili Live Special Event Thread"
        End If

        If _timeLimitEventThd.ThreadState = ThreadState.Unstarted Then
            _timeLimitEventThd.Start()
        End If
    End Sub
    Public Sub AsyncStopTimeLimitedEvent()
        If _timeLimitEventThd Is Nothing Then Return
        _timeLimitEventThd.Abort()
    End Sub
    Public Event NextEventGrabTime(ByVal time As Date)


    '获取用户信息
    Public Function SyncGetUserInfo() As JObject
        Dim netstr As New NetStream
        netstr.ReadWriteTimeout = 5000
        netstr.Timeout = 5000

        netstr.HttpGet("http://live.bilibili.com/User/getUserInfo?timestamp=" & Int(ToUnixTimestamp(Now) * 1000))

        Dim str As String = netstr.ReadResponseString
        netstr.Close()

        Debug.Print("Get User Info succeeded, response returned value:")
        Debug.Print(str)

        Return JsonConvert.DeserializeObject(str)
    End Function

    '发送弹幕
    Public Sub SyncSendComment(ByVal msg As String, Optional ByVal color As Color = Nothing, Optional ByVal fontsize As UInteger = 25, Optional ByVal roomid As Integer = -1)
        If roomid = -1 Then roomid = _RoomId
        If color.IsEmpty Then color = Color.White
        If roomid <= 0 Then Return

        Dim req As New NetStream
        Dim req_param As New Parameters
        req_param.Add("color", color.ToArgb And &HFFFFFF)
        req_param.Add("fontsize", fontsize)
        req_param.Add("mode", 1)
        req_param.Add("msg", msg)
        req_param.Add("rnd", rand.Next)
        req_param.Add("roomid", roomid)
        Try
            req.HttpPost("http://live.bilibili.com/msg/send", req_param)
            Dim rep As String = req.ReadResponseString

            Debug.Print("Send comment succeeded, response returned value:")
            Debug.Print(rep)

        Catch ex As Exception
            Debug.Print(ex.ToString)
        End Try
        req.Close()
    End Sub
End Class
''' <summary>
''' b站登录函数[附带RSA加密模块]
''' </summary>
''' <remarks></remarks>
Public Module Bilibili_Login
    Public Const LOGIN_URL As String = "https://passport.bilibili.com/login/dologin"
    Public Const LOGOUT_URL As String = "https://account.bilibili.com/login"
    Public Const BACKUP_LOGIN_URL As String = "https://passport.bilibili.com/ajax/miniLogin/login"
    Public Const LOGIN_PUBLIC_KEY As String = "https://passport.bilibili.com/login?act=getkey"
    Public Const BILIBILI_MAIN_PAGE As String = "http://www.bilibili.com"
    Public Const CAPTCHA_URL As String = "https://passport.bilibili.com/captcha"
    Public Const API_MY_INFO As String = "http://api.bilibili.com/myinfo"
    '2015/12/31  RSA 加密登录成功
    '2016/03/25  将域名 account.bilibili.com 换为 passport.bilibili.com ，登录成功

    ''' <summary>
    ''' 使用主站登录模块
    ''' </summary>
    ''' <param name="userid">用户ID</param>
    ''' <param name="pwd">密码</param>
    ''' <param name="captcha">验证码</param>
    ''' <returns>登录是否成功</returns>
    ''' <remarks></remarks>
    Public Function Login(ByVal userid As String, ByVal pwd As String, ByVal captcha As String, Optional ByRef login_result As String = Nothing) As Boolean
        Dim param As New Parameters
        param.Add("act", "login")
        param.Add("userid", userid)
        param.Add("keeptime", 604800)

        'Form1.DebugOutput("用户名: " & userid)

        'RSA加密
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000

        'Form1.DebugOutput("获取RSA公钥URL: " & LOGIN_PUBLIC_KEY)
        req.HttpGet(LOGIN_PUBLIC_KEY)
        Dim loginRequest As String = ReadToEnd(req.Stream)
        Dim loginRequest2 As JObject = JsonConvert.DeserializeObject(loginRequest)

        Dim rsaPublicKey As String = loginRequest2.Value(Of String)("key")
        Dim hash As String = loginRequest2.Value(Of String)("hash")
        req.Close()

        'Form1.DebugOutput("RSA公钥: " & rsaPublicKey)

        Dim rsa1 As New System.Security.Cryptography.RSACryptoServiceProvider
        rsa1.ImportParameters(RSA.ConvertFromPemPublicKey(rsaPublicKey))

        'Form1.DebugOutput("本地RSA XML数据: " & rsa1.ToXmlString(False))

        '哔站所谓的加密也不过如此嘛……第一次用了pwd=pwd+hash报错，第二次pwd=hash+pwd结果wwwww
        pwd = hash & pwd
        Dim tempPwd() As Byte = System.Text.Encoding.GetEncoding(DEFAULT_ENCODING).GetBytes(pwd)

        Dim password() As Byte = rsa1.Encrypt(tempPwd, False)

        pwd = Convert.ToBase64String(password)

        param.Add("pwd", pwd)

        'Form1.DebugOutput("加密后的密码: " & pwd)

        param.Add("vdcode", captcha)


        'Form1.DebugOutput("发送登录信息...")

        req.HttpPost(LOGIN_URL, param)
        Dim str As String = ReadToEnd(req.Stream)
        req.Close()

        If login_result IsNot Nothing Then
            login_result = str
        End If
        'Form1.DebugOutput("返回数据: " & str)

        Return CheckLogin()
    End Function

    ''' <summary>
    ''' 使用精简登录模块，若密码输入不出错，则不需输入验证码
    ''' </summary>
    ''' <param name="userid">用户ID</param>
    ''' <param name="pwd">密码</param>
    ''' <param name="captcha">验证码</param>
    ''' <returns>登录是否成功</returns>
    ''' <remarks></remarks>
    Public Function LoginBackup(ByVal userid As String, ByVal pwd As String, Optional ByVal captcha As String = "", Optional ByRef login_result As String = Nothing) As Boolean
        Dim param As New Parameters
        param.Add("userid", userid)

        'Form1.DebugOutput("用户名: " & userid)

        'RSA加密
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000

        'Form1.DebugOutput("获取RSA公钥URL: " & LOGIN_PUBLIC_KEY)
        req.HttpGet(LOGIN_PUBLIC_KEY)
        Dim loginRequest As String = ReadToEnd(req.Stream)
        Dim loginRequest2 As JObject = JsonConvert.DeserializeObject(loginRequest)

        Dim rsaPublicKey As String = loginRequest2.Value(Of String)("key")
        Dim hash As String = loginRequest2.Value(Of String)("hash")
        req.Close()

        'Form1.DebugOutput("RSA公钥: " & rsaPublicKey)

        Dim rsa1 As New System.Security.Cryptography.RSACryptoServiceProvider
        rsa1.ImportParameters(RSA.ConvertFromPemPublicKey(rsaPublicKey))

        'Form1.DebugOutput("本地RSA XML数据: " & rsa1.ToXmlString(False))

        pwd = hash & pwd
        Dim tempPwd() As Byte = System.Text.Encoding.GetEncoding(DEFAULT_ENCODING).GetBytes(pwd)

        Dim password() As Byte = rsa1.Encrypt(tempPwd, False)

        pwd = Convert.ToBase64String(password)

        param.Add("pwd", pwd)

        'Form1.DebugOutput("加密后的密码: " & pwd)

        param.Add("captcha", captcha)
        param.Add("keep", 1)


        'Form1.DebugOutput("发送登录信息...")


        req.HttpPost(BACKUP_LOGIN_URL, param)
        Dim str As String = ReadToEnd(req.Stream)
        req.Close()

        If login_result IsNot Nothing Then
            login_result = str
        End If
        'Form1.DebugOutput("返回数据: " & str.Replace(vbCr, "").Replace(vbLf, ""))

        Return CheckLogin()
    End Function
    ''' <summary>
    ''' 获取验证码
    ''' </summary>
    ''' <returns>返回验证码图像</returns>
    ''' <remarks></remarks>
    Public Function GetCaptchaImage() As Image
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000
        req.HttpGet(CAPTCHA_URL)
        Dim img As Image = Image.FromStream(req.Stream)
        req.Close()
        Return img
    End Function
    ''' <summary>
    ''' 查看登录是否成功
    ''' </summary>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function CheckLogin() As Boolean
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000
        req.HttpGet(API_MY_INFO)
        Dim str As String = ReadToEnd(req.Stream)
        req.Close()
        str = str.Replace(vbCr, "").Replace(vbLf, "")
        'Form1.DebugOutput("API请求: [登录信息]: " & API_MY_INFO & vbCrLf & JsonConvert.DeserializeObject(str).ToString)
        Return If(InStr(str, "-101"), False, True)
    End Function
    ''' <summary>
    ''' 退出登录
    ''' </summary>
    ''' <returns>返回当前是否登录</returns>
    ''' <remarks></remarks>
    Public Function LogOut() As Boolean
        Dim url As String = LOGOUT_URL
        Dim param As New Parameters
        param.Add("act", "exit")
        Dim req As New NetStream
        req.ReadWriteTimeout = 5000
        req.Timeout = 5000

        'Form1.DebugOutput("发送注销信息...")

        req.HttpGet(url, , param)
        Dim str As String = ReadToEnd(req.Stream)
        req.Close()

        'Form1.DebugOutput("返回数据:" & str)

        Return CheckLogin()
    End Function
End Module
