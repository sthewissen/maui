using Microsoft.Maui.Controls.Build.Tasks;
using NUnit.Framework;

namespace Microsoft.Maui.Controls.Xaml.UnitTests;

[XamlCompilation(XamlCompilationOptions.Skip)]
[XamlProcessing(XamlInflator.Runtime, true)]
public partial class Gh2063 : ContentPage
{
	public Gh2063() => InitializeComponent();

	[TestFixture]
	class Tests
	{
		[Test]
		public void DetailedErrorMessageOnMissingXmlnsDeclaration([Values] XamlInflator inflator)
		{
			if (inflator == XamlInflator.XamlC)
				Assert.Throws<BuildException>(() => MockCompiler.Compile(typeof(Gh2063)));
			if (inflator == XamlInflator.Runtime)
				Assert.Throws<XamlParseException>(() => new Gh2063(inflator));
			if (inflator == XamlInflator.SourceGen)
			{
				var result = MockSourceGenerator.RunMauiSourceGenerator(MockSourceGenerator.CreateMauiCompilation(), typeof(Gh2063));
				Assert.That(result.Diagnostics, Is.Not.Empty);
			}				
		}
	}
}