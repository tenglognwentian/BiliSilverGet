﻿# BiliSilverGet
b站直播的瓜子搜刮机

V1.7 测试版<br/>
Project 2016 填坑计划
### 目前的代码填坑走向
> 代码规范（异常处理模块）重写

### 编译&运行
1. 点击Clone或者下载zip获取代码
2. 使用Visual Studio 2015编译（其实应该没什么所谓，只要支持.net 4.0就好了，之前用vs2013和vs2012都编译过）
3. 选择 `x86平台` ，生成目标程序，运行就好了
4. 按照提示登录，PS：验证码接口一直懒着没做，因为是用miniLogin的所以在一般情况下是免验证码的 `注意：该登陆算法为RSA加密， * 非官方开放接口 * ，开发者不承担用户因账号丢失所损失的一切费用`
5. 勾选自己需要的功能就ok了，如果不管用的话可以尝试再次勾选
6. 额。。win10的开机启动目前还是迷

### 说明
- b站登录接口是自己抓包的，想要了解更多，请见`登录说明`
- 有bug可以汇报，当然也希望有人能够帮忙修一修bug以及整理一下乱成麻花的程序
- 以后或许还会更新b站直播的活动，不过要看个人时间允不允许了

### 更新
v1.7
- 之前使用的手机版瓜子搜刮api被提示 `api sign invalid(code=-3)` ，再次换成电脑版接口+OCR验证码识别，初步测试运行良好
- 更换弹幕发送的域名

v1.6
- 支持竞猜数据实时更新
- 修改b站活动：红叶祭
- 推出第一个release，虽然还是alpha版本

v1.5
- bug修复
- 支持更多的直播间消息种类

v1.4
- 可以发送弹幕了……
- 也可以看用户信息了……

v1.3
- UI再改版
- 道具可以自己送了，有时候无脑就是干的感觉可真糟糕
- 支持限时活动：团扇get da★ze！ 屠龙宝刀，点击就送！
- 支持up领取的特定房间号id，兼容原cid
- 把挂机领经验的功能干出来了……
- 增加了icon……决定就是你了！银瓜子！

v1.2
- 不知道有没有修复过bug反正就先更新一发（笑）

v1.1
- 支持直播录播功能
- 新增直播弹幕查看 
- UI改版
- 多线程代码优化

### License
GNU GPLv3

<p align="right">
Project 2016<br/>
Pandasxd
</p>
