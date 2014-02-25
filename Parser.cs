using System;
using KitchenPC.Context;

namespace KitchenPC.Parser
{
   class Parser
   {
      static void Main()
      {
         // Initialize KitchenPC
         KPCContext.Initialize(Configuration<StaticContext>.Build
            .Context(StaticContext.Configure
               .DataDirectory(@"C:\KitchenPC\src\SampleData\")
            ).Create());

         // Initialize Parser Factory
         ParserFactory.Initialize(
            KPCContext.Current,
            typeof(Parsers.hRecipeParser).Assembly,
            typeof(Parsers.hRecipeParser));

         // Test URIs
         var urls = new[]
         {
            // new Uri("http://allrecipes.com/recipe/classic-peanut-butter-cookies/"),

            new Uri("http://www.food.com/recipe/aunt-eileens-sauce-pan-brownies-256306"),
            new Uri("http://www.food.com/recipe/lower-fat-banana-nut-chip-muffins-199237"),
            new Uri("http://www.food.com/recipe/grated-apple-cinnamon-cake-183836"),
            new Uri("http://www.food.com/recipe/buttery-ricotta-cookies-339259")
         };

         urls.ForEach(u =>
         {
            var parser = ParserFactory.GetParser(u);
            var result = parser.Parse(u);

            if (result.Result == ParserResult.Status.Success)
               Console.WriteLine("Successfully parsed recipe: {0}", result.Recipe.Title);
            else
               Console.WriteLine("Could not parse {0}", u);
         });

         Console.WriteLine("\nDone! [Press Any Key]");
         Console.ReadLine();
      }
   }
}
