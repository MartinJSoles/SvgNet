/*
	Copyright © 2003 RiskCare Ltd. All rights reserved.
	Copyright © 2010 SvgNet & SvgGdi Bridge Project. All rights reserved.
	Copyright © 2015 Rafael Teixeira, Mojmír Němeček, Benjamin Peterson and Other Contributors

	Original source code licensed with BSD-2-Clause spirit, treat it thus, see accompanied LICENSE for more
*/


using System;
using System.Collections;
using System.IO;
using System.Xml;
using System.Reflection;
using SvgNet.SvgElements;

namespace SvgNet
{
	/// <summary>
	/// Static methods to produce/write/copy Svg documents reside in this class.
	/// </summary>
	public class SvgFactory
	{
		private SvgFactory()
		{
		}

		private static Hashtable _elementNameDictionary;

		/// <summary>
		/// Given an xml document and (optionally) a particular element to start from, read the xml nodes and construct
		/// a tree of <see cref="SvgElement"/> objects.  Xml tags that do not correspond to a particular class will be
		/// represented by an <see cref="SvgGenericElement"/>.  This means that literally any XML input can be read in
		/// and written out, even if it has nothing to do with Svg.  More usefully, it means that new and unsupported tags
		/// and attributes will be represented in the <c>SvgElement</c> tree and written out correctly even if SvgNet does
		/// not understand them.
		/// </summary>
		/// <param name="doc"></param>
		/// <param name="el"></param>
		/// <returns></returns>
		public static SvgElement LoadFromXML(XmlDocument doc, XmlElement el)
		{
			if (el == null)
			{
				foreach(XmlNode noddo in doc.ChildNodes)
				{
					if (noddo.GetType() == typeof(XmlElement))
					{
						el = (XmlElement)noddo;
						break;
					}
				}
			}

			if (el == null)
				return null;

			if (_elementNameDictionary == null)
			{
				BuildElementNameDictionary();
			}

			Type t = (Type)_elementNameDictionary[el.Name];

			SvgElement e = (SvgElement)t.GetConstructor(new System.Type[0]).Invoke(new object[0]);

			RecLoadFromXML(e, doc, el);

			return e;
		}

		/// <summary>
		/// Used by LoadFromXML
		/// </summary>
		/// <param name="e"></param>
		/// <param name="doc"></param>
		/// <param name="el"></param>
		private static void RecLoadFromXML(SvgElement e, XmlDocument doc, XmlElement el)
		{
			e.ReadXmlElement(doc, el);

			foreach(XmlNode noddo in el.ChildNodes)
			{
				if (noddo.GetType() == typeof(XmlElement))
				{
					XmlElement childXml = (XmlElement)noddo;

					Type t = (Type)_elementNameDictionary[childXml.Name];

					SvgElement childSvg=null;

					if (t == null)
					{
						childSvg = new SvgGenericElement(childXml.Name);
					}
					else
					{
						childSvg = (SvgElement)t.GetConstructor(new System.Type[0]).Invoke(new object[0]);
					}

					e.AddChild(childSvg);

					RecLoadFromXML(childSvg, doc, childXml);
				}
				else if (noddo.GetType() == typeof(XmlText))
				{
					XmlText xt = (XmlText)noddo;

					TextNode tn = new TextNode(xt.InnerText);

					e.AddChild(tn);
				}
			}
		}

		/// <summary>
		/// Used by LoadFromXML
		/// </summary>
		private static void BuildElementNameDictionary()
		{

			_elementNameDictionary = new Hashtable();

			Assembly asm = Assembly.GetExecutingAssembly();

			Type[] ta = asm.GetExportedTypes();

			foreach(Type t in ta)
			{
				if (t.IsSubclassOf(typeof(SvgElement)))
				{
					SvgElement e = (SvgElement)t.GetConstructor(new System.Type[0]).Invoke(new object[0]);

					if (e.Name != "?" /* default name of abstract SvgElements */)
					{
						_elementNameDictionary[e.Name] = e.GetType();
					}
				}
			}
		
		}

		/// <summary>
		/// Helper function to compress long xml attributes into entities.
		/// <para>
		/// This would work on any XML, it is not SVG specific, so it should eventually be in some 'xml tools' class.
		/// </para>
		/// </summary>
		/// <param name="doc"></param>
		/// <param name="el"></param>
		/// <returns>A string of entities which can be inserted into the DOCTYPE when the document is written.</returns>
		public static string CompressXML(XmlDocument doc, XmlElement el)
		{
			Hashtable entities = new Hashtable();
			Hashtable singletons = new Hashtable();

			int idx = 0;
			RecCompXML(entities, singletons, doc, el, ref idx);

			// "Uncompress" attribute values with only one reference as it would actually
			// make the resulting XML bigger
			foreach(DictionaryEntry pair in singletons)
			{
				var attributeValue = (string)pair.Key;
				var singleton = (EntitySingleton)pair.Value;

				// This is an inverse behavior of what RecCompXML did
				singleton.Element.RemoveAttribute(singleton.AttributeName);
				XmlAttribute attr = doc.CreateAttribute(singleton.AttributeName);
				attr.Value = attributeValue;
				singleton.Element.SetAttributeNode(attr);

				entities.Remove(attributeValue);
			}

			var ents = new System.Text.StringBuilder();

			if (entities.Count > 0)
			{
				ents.Append("\n");

				foreach (string key in entities.Keys)
				{
					ents.Append("<!ENTITY ");
					ents.Append(entities[key]);
					ents.Append(" '");
					ents.Append(key.Replace("%", "&#37;"));//I guess we need to escape fully, but surely no other special char can appear?
					ents.Append("'>");
				}

				ents.Append("\n");
			}

			return ents.ToString();
		}

		private struct EntitySingleton
		{
			public XmlElement Element;
			public string AttributeName;
		}

		/// <summary>
		/// Used by CompressXML
		/// </summary>
		/// <param name="counts">Map of attribute to number of occurrences -- could be used to improve algorithm</param>
		/// <param name="entities">Map of attribute to entity name</param>
		/// <param name="doc"></param>
		/// <param name="el"></param>
		/// <param name="idx">Number that is incremented to provide new entity names</param>
		private static void RecCompXML(Hashtable entities, Hashtable singletons, XmlDocument doc, XmlElement el, ref int idx)
		{	
			ArrayList keys = new ArrayList();

			foreach(XmlAttribute att in el.Attributes)
			{
				keys.Add(att.Name);
			}

			foreach(string s in keys)
			{
				string val = el.Attributes[s].Value;
	
				if (val.Length > 30)
				{
					string entname;

					if(entities[val] == null)
					{
						idx += 1;
						entname = "E"+idx.ToString();
						entities[val] = entname;

						singletons[val] = new EntitySingleton() { Element = el, AttributeName = s };
					}
					else
					{
						entname = (string)entities[val];

						singletons.Remove(val);
					}

					XmlAttribute attr = doc.CreateAttribute(s);
					attr.AppendChild(doc.CreateEntityReference(entname));
					el.SetAttributeNode(attr);
				}

			}

			foreach(XmlNode ch in el.ChildNodes)
			{
				if (ch.GetType() == typeof(XmlElement))
					RecCompXML(entities, singletons, doc, (XmlElement)ch, ref idx);
			}
		}
	
		/// <summary>
		/// Create a complete deep copy of the given tree of <c>SvgElement</c> objects.
		/// A new set of elements is created, and if the attributes are cloneable they are deep-copied too.
		/// Since strings and all SvgType classes are cloneable, the new tree is independant of the old.
		/// </summary>
		/// <param name="el"></param>
		/// <returns></returns>
		public static SvgElement CloneElement(SvgElement el)
		{
			SvgElement clone = (SvgElement)el.GetType().GetConstructor(new System.Type[0]).Invoke(new object[0]);

			foreach(string key in el.Attributes.Keys)
			{
				object o = el[key];
				if (typeof(ICloneable).IsInstanceOfType(o))
					clone[key] = ((ICloneable)o).Clone();
				else
					clone[key] = o;
			}

			foreach(SvgElement ch in el.Children)
			{
				clone.AddChild(CloneElement(ch));
			}

			return clone;
		}
	}
}
