using System;
using System.Collections.Generic;
using System.Reflection;
using KitchenPC.Context;
using KitchenPC.Recipes;

namespace KitchenPC.Parser
{
   public interface IParser
   {
      NLP.Parser NlpParser { get; set; }
      ParserResult Parse(Uri page);
   }

   public class ParserAttribute : Attribute
   {
      public string Domain { get; private set; }

      public ParserAttribute(string domain)
      {
         Domain = domain;
      }
   }

   public class ParserResult
   {
      public enum Status { Success, UnknownIngredient, MissingData, BadData }

      public Status Result { get; private set; }
      public IEnumerable<String> UnknownIngredients { get; set; }
      public Recipe Recipe { get; private set; }

      public ParserResult(Status result)
      {
         Result = result;
      }

      public ParserResult(Recipe recipe)
      {
         Result = Status.Success;
         Recipe = recipe;
      }
   }

   public static class ParserFactory
   {
      private static IKPCContext context;
      private static Dictionary<String, Type> parserMap;
      private static Type defaultParser;

      public static void Initialize(IKPCContext context, Assembly assembly, Type defaultParser)
      {
         ParserFactory.context = context;

         parserMap = new Dictionary<string, Type>();
         ParserFactory.defaultParser = defaultParser;
         
         var types = assembly.GetTypes();
         foreach (var t in types)
         {
            var att = t.GetCustomAttributes(typeof(ParserAttribute), false);
            foreach(var a in att)
            {
               var domain = ((ParserAttribute)a).Domain.Trim().ToLower();
               if (parserMap.ContainsKey(domain))
               {
                  throw new DuplicateParser(domain);
               }

               parserMap.Add(domain, t);
            }
         }
      }

      public static IParser GetParser(string uri)
      {
         return GetParser(new Uri(uri));
      }

      public static IParser GetParser(Uri uri)
      {
         Type t;
         if (!parserMap.TryGetValue(uri.Host.ToLower(), out t))
         {
            t = defaultParser;
         }

         var constructor = t.GetConstructor(new Type[0]);
         var p = constructor.Invoke(null) as IParser;
         p.NlpParser = context.Parser;
         return p;
      }
   }
}
