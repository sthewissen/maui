using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.Controls.SourceGen;

static class GeneratorHelpers
{
	public static string EscapeIdentifier(string identifier)
	{
		var kind = SyntaxFacts.GetKeywordKind(identifier);
		return kind == SyntaxKind.None
			? identifier
			: $"@{identifier}";
	}

	public static ProjectItem? ComputeProjectItem((AdditionalText, AnalyzerConfigOptionsProvider) tuple, CancellationToken cancellationToken)
	{
		var (additionalText, optionsProvider) = tuple;
		var fileOptions = optionsProvider.GetOptions(additionalText);
		if (!fileOptions.TryGetValue("build_metadata.additionalfiles.GenKind", out string? kind) || kind is null)
		{
			return null;
		}

		fileOptions.TryGetValue("build_metadata.additionalfiles.TargetPath", out var targetPath);
		fileOptions.TryGetValue("build_metadata.additionalfiles.ManifestResourceName", out var manifestResourceName);
		fileOptions.TryGetValue("build_metadata.additionalfiles.RelativePath", out var relativePath);
		fileOptions.TryGetValue("build_property.targetframework", out var targetFramework);
		fileOptions.TryGetValue("build_property._MauiForceXamlSourcegen", out var forceSourceGen);
		return new ProjectItem(additionalText, targetPath: targetPath, relativePath: relativePath, manifestResourceName: manifestResourceName, kind: kind, targetFramework: targetFramework)
		{
			ForceSourceGen = forceSourceGen == true.ToString()
		};
	}

	public static XamlProjectItemForIC? ComputeXamlProjectItemForIC(ProjectItem? projectItem, CancellationToken cancellationToken)
	{
		var text = projectItem?.AdditionalText.GetText(cancellationToken);
		if (text == null)
		{
			return null;
		}
		try { 
			return new XamlProjectItemForIC(projectItem!, ParseXaml(text.ToString()));
		}
		catch (Exception e)
		{
			return new XamlProjectItemForIC(projectItem!, e);
		}
	}

	static SGRootNode? ParseXaml(string xaml)
    {
		using (var stringreader = new StringReader(xaml))
        using (var reader = XmlReader.Create(stringreader))
        {
            while (reader.Read())
            {
                //Skip until element
                if (reader.NodeType == XmlNodeType.Whitespace)
                    continue;
                if (reader.NodeType != XmlNodeType.Element)
                {
                    Debug.WriteLine("Unhandled node {0} {1} {2}", reader.NodeType, reader.Name, reader.Value);
                    continue;
                }

				var rootnode = new SGRootNode(new XmlType(reader.NamespaceURI, reader.Name, XamlParser.GetTypeArguments(reader)), /*typeReference, */(IXmlNamespaceResolver)reader, ((IXmlLineInfo)reader).LineNumber, ((IXmlLineInfo)reader).LinePosition);
                XamlParser.ParseXaml(rootnode, reader);

                return rootnode;
            }
        }
        return null;
    }

	public static XamlProjectItemForCB? ComputeXamlProjectItemForCB(ProjectItem? projectItem, CancellationToken cancellationToken)
	{
		var text = projectItem?.AdditionalText.GetText(cancellationToken);
		if (text == null)
			return null;

		XmlNode? root;
		XmlNamespaceManager nsmgr;
		try {
			(root, nsmgr) = LoadXmlDocument(text, cancellationToken);
		} catch (Exception xe) {
			return new XamlProjectItemForCB(projectItem!, xe);
		}

		if (root == null)
			return null;

		ApplyTransforms(root, projectItem!.TargetFramework, nsmgr);

		foreach (XmlAttribute attr in root.Attributes)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (attr.Name == "xmlns")
				nsmgr.AddNamespace("", attr.Value); //Add default xmlns

			if (attr.Prefix != "xmlns")
				continue;

			nsmgr.AddNamespace(attr.LocalName, attr.Value);
		}

		return new XamlProjectItemForCB(projectItem, root, nsmgr);
	}

	public static (XmlNode?, XmlNamespaceManager) LoadXmlDocument(SourceText text, CancellationToken cancellationToken)
	{
		var xmlDoc = new XmlDocument();
		xmlDoc.LoadXml(text.ToString());


#pragma warning disable CS0618 // Type or member is obsolete
		if (xmlDoc.DocumentElement.NamespaceURI == XamlParser.FormsUri)
			throw new Exception($"{XamlParser.FormsUri} is not a valid namespace. Use {XamlParser.MauiUri} instead");
#pragma warning restore CS0618 // Type or member is obsolete

		cancellationToken.ThrowIfCancellationRequested();

		var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
		nsmgr.AddNamespace("__f__", XamlParser.MauiUri);

		var root = xmlDoc.SelectSingleNode("/*", nsmgr);
		return (root, nsmgr);
	}

	public static AssemblyCaches GetAssemblyAttributes(Compilation compilation, CancellationToken cancellationToken)
	{
		// [assembly: XmlnsDefinition]
		INamedTypeSymbol? xmlnsDefinitonAttribute = compilation.GetTypesByMetadataName(typeof(XmlnsDefinitionAttribute).FullName)
			.SingleOrDefault(t => t.ContainingAssembly.Identity.Name == "Microsoft.Maui.Controls");

		// [assembly: InternalsVisibleTo]
		INamedTypeSymbol? internalsVisibleToAttribute = compilation.GetTypeByMetadataName(typeof(InternalsVisibleToAttribute).FullName);

		if (xmlnsDefinitonAttribute is null || internalsVisibleToAttribute is null)
		{
			return AssemblyCaches.Empty;
		}

		var xmlnsDefinitions = new List<XmlnsDefinitionAttribute>();
		var internalsVisible = new List<IAssemblySymbol>();

		internalsVisible.Add(compilation.Assembly);

		// load from references
		foreach (var reference in compilation.References)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol symbol)
			{
				continue;
			}

			foreach (var attr in symbol.GetAttributes())
			{
				if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, xmlnsDefinitonAttribute))
				{
					// [assembly: XmlnsDefinition]
					var xmlnsDef = new XmlnsDefinitionAttribute(attr.ConstructorArguments[0].Value as string, attr.ConstructorArguments[1].Value as string);
					if (attr.NamedArguments.Length == 1 && attr.NamedArguments[0].Key == nameof(XmlnsDefinitionAttribute.AssemblyName))
					{
						xmlnsDef.AssemblyName = attr.NamedArguments[0].Value.Value as string;
					}
					else
					{
						xmlnsDef.AssemblyName = symbol.Name;
					}

					xmlnsDefinitions.Add(xmlnsDef);
				}
				else if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, internalsVisibleToAttribute))
				{
					// [assembly: InternalsVisibleTo]
					if (attr.ConstructorArguments[0].Value is string assemblyName && new AssemblyName(assemblyName).Name == compilation.Assembly.Identity.Name)
					{
						internalsVisible.Add(symbol);
					}
				}
			}
		}
		return new AssemblyCaches(xmlnsDefinitions, internalsVisible);
	}

	static void ApplyTransforms(XmlNode node, string? targetFramework, XmlNamespaceManager nsmgr)
	{
		SimplifyOnPlatform(node, targetFramework, nsmgr);
	}

	static void SimplifyOnPlatform(XmlNode node, string? targetFramework, XmlNamespaceManager nsmgr)
	{
		//remove OnPlatform nodes if the platform doesn't match, so we don't generate field for x:Name of elements being removed
		if (targetFramework == null)
		{
			return;
		}

		string? target = null;
		targetFramework = targetFramework.Trim();
		if (targetFramework.IndexOf("-android", StringComparison.OrdinalIgnoreCase) != -1)
		{
			target = "Android";
		}

		if (targetFramework.IndexOf("-ios", StringComparison.OrdinalIgnoreCase) != -1)
		{
			target = "iOS";
		}

		if (targetFramework.IndexOf("-macos", StringComparison.OrdinalIgnoreCase) != -1)
		{
			target = "macOS";
		}

		if (targetFramework.IndexOf("-maccatalyst", StringComparison.OrdinalIgnoreCase) != -1)
		{
			target = "MacCatalyst";
		}

		if (target == null)
		{
			return;
		}

		//no need to handle {OnPlatform} markup extension, as you can't x:Name there
		var onPlatformNodes = node.SelectNodes("//__f__:OnPlatform", nsmgr);
		foreach (XmlNode onPlatformNode in onPlatformNodes)
		{
			var onNodes = onPlatformNode.SelectNodes("__f__:On", nsmgr);
			foreach (XmlNode onNode in onNodes)
			{
				var platforms = onNode.SelectSingleNode("@Platform");
				var plats = platforms.Value.Split(',');
				var match = false;

				foreach (var plat in plats)
				{
					if (string.IsNullOrWhiteSpace(plat))
					{
						continue;
					}

					if (plat.Trim() == target)
					{
						match = true;
						break;
					}
				}
				if (!match)
				{
					onNode.ParentNode.RemoveChild(onNode);
				}
			}
		}
	}
	
	public static IDictionary<XmlType, ITypeSymbol> GetTypeCache(Compilation compilation, CancellationToken cancellationToken)
	{
		return new Dictionary<XmlType, ITypeSymbol>();
	}
}
