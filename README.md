

Tiema Platform

An industrial application plugin platform that supports full-stack plugin development from PLC control to ERP business.



Goal: Build a complete Tiema full-chain minimum demonstration system within 10 days
目标: 10天内构建一个完整的Tiema全链路最小演示系统

Modbus sensor temperature collection plugin, writes to Tag: Plant/Temperature
TemperatureLogic plugin checks this temperature value. If the temperature exceeds 30 degrees, it sends an alarm message to the alarm subscribers.
Simple AlarmSubscriber plugin receives the alarm message and prints it to the console.

Modbus sensor采集温度插件，写入 Tag:Plant/Temperature
TemperatureLogic插件 ，检查这个温度值，如果温度高于30度，则发送一个报警信息到报警订阅者
Simple AlarmSubscriber插件，接收报警信息并打印到控制台



(Complete完成 )

 1. Project Structure Setup / 项目结构搭建  

 2. Container Engine Basics / 容器引擎基础   

 3. rack and module added  / 增加了机架和模块功能

4. Backplane Routing Implementation / Backplane路由实现 

5.  Tag System Implementation / Tag系统实现


6. Inter-Plugin Data Flow Verification / 插件间数据流验证

7. gRPC Communication Layer / gRPC通信层



(Next 下一步 )



grpc已经就绪，可以进行跨进程通信测试了。



[] Cross-Process Communication Testing / 跨进程通信测试

[] Full-Chain Integration / 全链路集成

[] Dockerized Deployment / Docker化部署

[] Documentation Improvement / 文档完善



🤝 Contributing
We welcome Issues and Pull Requests! The project is licensed under the MIT License.

📞 Contact
GitHub Issues: Submit issues or suggestions

Email: 896294580@qq.com

