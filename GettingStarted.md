

1. Architecture

The system consists of a container that holds plugins.

```csharp
public interface IPluginContainer
{
    void Run();
    void LoadPlugins();

    ITagService TagService { get; }
    IMessageService MessageService { get; }
}
```

As we can see from the container interface, it provides the functionality to load plugins and two basic services: 
`TagService` and `MessageService`.
- `TagService`: allows plugins to share variables and provides data exchange capabilities.
- `MessageService`: enables communication between plugins.

2. Plugins

Plugins provide specific functionalities needed by the system. They can access the framework's capabilities through 
`IPluginContext`:

```csharp
public interface IPluginContext
{
    IPluginContainer Container { get; }
    ITagService Tags { get; }
    IMessageService Messages { get; }
}
```

Through this interface, we can see that plugins can access the container's services. I will optimize this in a 
future version by having `PluginBase` inherit from `ICycleContext` directly, as `ICycleContext` also defines these
two service interfaces, which is somewhat redundant.

Now, let's take a look at the plugin interface:

```csharp
public interface IPlugin
{
    string Name { get; }
    void Initialize(IPluginContext context);
    void Execute(ICycleContext context);
}
```

Plugins have two important methods: `Initialize` and `Execute`, which are used for plugin initialization and execution 
logic, respectively.
- The `Initialize` method is called when the plugin is loaded, passing in `IPluginContext`, which allows the plugin to 
access the container's capabilities.
- The `Execute` method is called when the plugin is executed, passing in `ICycleContext`, enabling the plugin to access 
- `TagService` and `MessageService` during its execution.

(In the future, I will remove the `context` parameter from the `Execute` method, as plugins can already obtain the context
through the `Initialize` method, making it simpler.)

3. Execution Process

After the container starts, it loads all the plugins and calls each plugin's `Initialize` method, passing in `IPluginContext`.
When a specific plugin needs to be executed, the container calls that plugin's `Execute` method, passing in `ICycleContext`.

In the future, I will remove the external scheduling of the `Execute` method, allowing plugins to run independently and drive
the collaboration between plugins through messages and tags.
The advantage of this design is that it reduces the coupling between plugins, allowing them to run independently and achieve
collaboration and data exchange through messages and tags.

4. Summary

Through the design of the container and plugins, a flexible plugin system is realized, where plugins can use the services 
provided by the container to achieve complex functionalities.
I will continue to improve this system in the future, adding more features and optimizing the design to make the plugin 
system more powerful and user-friendly.

 Build and Run Tiema.Runtime to see this architecture in action!
