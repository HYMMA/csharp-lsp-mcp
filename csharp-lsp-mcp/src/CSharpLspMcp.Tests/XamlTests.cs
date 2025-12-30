using CSharpLspMcp.Xaml;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CSharpLspMcp.Tests;

public class XamlParserTests
{
    private readonly XamlParser _parser = new();

    [Fact]
    public void Parse_ValidXaml_ReturnsRootElement()
    {
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><Button Content=\"Click Me\" /></Grid></Window>";

        var result = _parser.Parse(xaml);

        Assert.NotNull(result.Root);
        Assert.Equal("Window", result.Root.Name);
        Assert.Empty(result.ParseErrors);
    }

    [Fact]
    public void Parse_ExtractsClassName()
    {
        var xaml = "<Window x:Class=\"MyApp.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"></Window>";

        var result = _parser.Parse(xaml);

        Assert.Equal("MainWindow", result.ClassName);
        Assert.Equal("MyApp", result.ClassNamespace);
    }

    [Fact]
    public void Parse_ExtractsNamedElements()
    {
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><StackPanel><TextBox x:Name=\"txtInput\" /><Button x:Name=\"btnSubmit\" Content=\"Submit\" /><Label Name=\"lblStatus\" /></StackPanel></Window>";

        var result = _parser.Parse(xaml);

        Assert.Equal(3, result.NamedElements.Count);
        Assert.Contains(result.NamedElements, n => n.Name == "txtInput" && n.Type == "TextBox");
        Assert.Contains(result.NamedElements, n => n.Name == "btnSubmit" && n.Type == "Button");
        Assert.Contains(result.NamedElements, n => n.Name == "lblStatus" && n.Type == "Label");
    }

    [Fact]
    public void Parse_ExtractsBindings()
    {
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><StackPanel><TextBox Text=\"{Binding UserName}\" /><TextBlock Text=\"{Binding Path=Status, Mode=OneWay}\" /><Button Command=\"{Binding SaveCommand}\" /></StackPanel></Window>";

        var result = _parser.Parse(xaml);

        Assert.Equal(3, result.Bindings.Count);
        Assert.Contains(result.Bindings, b => b.Path == "UserName");
        Assert.Contains(result.Bindings, b => b.Path == "Status" && b.Mode == "OneWay");
        Assert.Contains(result.Bindings, b => b.Path == "SaveCommand");
    }

    [Fact]
    public void Parse_ExtractsResourceReferences()
    {
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><StackPanel><Button Style=\"{StaticResource ButtonStyle}\" /><TextBlock Foreground=\"{DynamicResource TextBrush}\" /></StackPanel></Window>";

        var result = _parser.Parse(xaml);

        Assert.Equal(2, result.ResourceReferences.Count);
        Assert.Contains(result.ResourceReferences, r => r.Key == "ButtonStyle" && r.IsStatic);
        Assert.Contains(result.ResourceReferences, r => r.Key == "TextBrush" && !r.IsStatic);
    }

    [Fact]
    public void Parse_ExtractsElementNameBindings()
    {
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><StackPanel><Slider x:Name=\"slider\" /><TextBlock Text=\"{Binding Value, ElementName=slider}\" /></StackPanel></Window>";

        var result = _parser.Parse(xaml);

        var binding = result.Bindings.FirstOrDefault(b => b.ElementName == "slider");
        Assert.NotNull(binding);
        Assert.Equal("Value", binding.Path);
    }

    [Fact]
    public void Parse_InvalidXaml_ReturnsParseError()
    {
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Button></Window>";

        var result = _parser.Parse(xaml);

        Assert.NotEmpty(result.ParseErrors);
        Assert.Contains(result.ParseErrors, e => e.Severity == XamlDiagnosticSeverity.Error);
    }

    [Fact]
    public void Parse_InvalidXaml_RecoversClassName()
    {
        var xaml = "<Window x:Class=\"MyApp.MainWindow\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Button></Window>";

        var result = _parser.Parse(xaml);

        Assert.NotEmpty(result.ParseErrors);
        Assert.Equal("MainWindow", result.ClassName);
        Assert.Equal("MyApp", result.ClassNamespace);
    }

    [Fact]
    public void Parse_AttachedProperty_CapturesOwnerAndProperty()
    {
        var xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Button Grid.Row=\"1\" /></Grid>";

        var result = _parser.Parse(xaml);

        var button = result.Root?.Children.FirstOrDefault();
        Assert.NotNull(button);
        var attached = button.Attributes.FirstOrDefault(a => a.Name.Contains("."));
        Assert.NotNull(attached);
        Assert.True(attached.IsAttached);
        Assert.Equal("Grid", attached.OwnerType);
        Assert.Equal("Row", attached.PropertyName);
    }

    [Fact]
    public void Parse_ExtractsNamespaces()
    {
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:MyApp.ViewModels\" xmlns:controls=\"clr-namespace:MyApp.Controls;assembly=MyApp.Controls\"></Window>";

        var result = _parser.Parse(xaml);

        Assert.Contains(result.Namespaces, kvp => kvp.Key == "local" && kvp.Value.Contains("MyApp.ViewModels"));
        Assert.Contains(result.Namespaces, kvp => kvp.Key == "controls" && kvp.Value.Contains("MyApp.Controls"));
    }
}

public class XamlAnalyzerTests
{
    [Fact]
    public async Task Analyze_EmptyBindingPath_ProducesInfoDiagnostic()
    {
        var logger = LoggerFactory.Create(_ => { }).CreateLogger<XamlAnalyzer>();
        var analyzer = new XamlAnalyzer(logger);
        var xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><TextBox Text=\"{Binding}\" /></Window>";

        var result = await analyzer.AnalyzeAsync("test.xaml", xaml, projectPath: null);

        Assert.Contains(result.Diagnostics, d => d.Code == "XAML004");
    }
}

public class XamlDiagnosticTests
{
    [Fact]
    public void XamlDiagnostic_CanBeCreated()
    {
        var diagnostic = new XamlDiagnostic
        {
            Message = "Test error",
            Severity = XamlDiagnosticSeverity.Error,
            Line = 10,
            Column = 5,
            Code = "XAML001"
        };

        Assert.Equal("Test error", diagnostic.Message);
        Assert.Equal(XamlDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(10, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
    }

    [Fact]
    public void XamlBinding_CanBeCreated()
    {
        var binding = new XamlBinding
        {
            Path = "UserName",
            Line = 15,
            Column = 20,
            Mode = "TwoWay",
            ElementName = "txtInput"
        };

        Assert.Equal("UserName", binding.Path);
        Assert.Equal("TwoWay", binding.Mode);
        Assert.Equal("txtInput", binding.ElementName);
    }

    [Fact]
    public void XamlResource_CanBeCreated()
    {
        var resource = new XamlResource
        {
            Key = "MyStyle",
            Type = "Style",
            Line = 5,
            Column = 10,
            IsStyle = true,
            TargetType = "Button"
        };

        Assert.Equal("MyStyle", resource.Key);
        Assert.True(resource.IsStyle);
        Assert.Equal("Button", resource.TargetType);
    }
}
