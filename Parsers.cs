using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using KitchenPC.Ingredients;
using KitchenPC.Recipes;

namespace KitchenPC.Parser.Parsers
{
   class hRecipeParser : IParser
   {
      public NLP.Parser NlpParser { get; set; }

      public static short ParseDuration(string vt)
      {
         try
         {
            var timespan = System.Xml.XmlConvert.ToTimeSpan(vt);
            return timespan.TotalMinutes > Int16.MaxValue ? Int16.MaxValue : Convert.ToInt16(timespan.TotalMinutes);
         }
         catch (FormatException)
         {
            throw new BadFormatException("Invalid duration: " + vt);
         }
      }

      protected HtmlNode RecursiveSearch(HtmlNode start, Func<HtmlNode, bool> expression)
      {
         var match = expression(start);
         if(match) return start;

         return start.ChildNodes
            .Select(child => RecursiveSearch(child, expression))
            .FirstOrDefault(attempt => attempt != null);
      }

      protected IEnumerable<HtmlNode> RecursiveCollect(HtmlNode start, Func<HtmlNode, bool> expression)
      {
         var match = expression(start);
         if (match)
            yield return start;

         foreach (var child in start.ChildNodes)
         {
            var attempt = RecursiveCollect(child, expression);

            foreach(var a in attempt)
               yield return a;
         }
      }

      protected HtmlNode FindNodeById(HtmlNode start, string id)
      {
         var node = RecursiveSearch(start, n =>
         {
            if (!n.Attributes.Contains("id"))
               return false;

            return String.CompareOrdinal(n.Attributes["id"].Value.Trim(), id) == 0;
         });

         return node;
      }

      protected HtmlNode FindNodeByClass(HtmlNode start, string className)
      {
         var node = RecursiveSearch(start, n =>
         {
            if (n == null || n.Attributes == null || !n.Attributes.Contains("class"))
               return false;

            var classes = n.Attributes["class"].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return classes.Contains(className, StringComparer.InvariantCultureIgnoreCase);
         });

         return node;
      }

      protected HtmlNode[] FindNodesByClass(HtmlNode start, string className)
      {
         var nodes = RecursiveCollect(start, n =>
            {
               if (!n.Attributes.Contains("class"))
                  return false;

               var classes = n.Attributes["class"].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
               return classes.Contains(className, StringComparer.InvariantCultureIgnoreCase);
            }).ToArray();

         return nodes;

      }

      protected virtual string ParseTitle(HtmlNode document)
      {
         //Required field, look for an element with the class name "fn" (might have other classes too) and parse any child text
         //Note: AllRecipes puts a span within the fn tag, we need to support this

         var fn = FindNodeByClass(document, "fn");
         if (fn == null)
         {
            throw new MissingNodeException("Document does not contain required node with 'fn' class.");
         }

         return fn.InnerText.Trim();
      }

      protected static bool ParseRange(string servings, out byte ret)
      {
         ret = 0;

         var match = Regex.Match(servings, @"\s*(?<low>\d+)\s*(-|to)\s*(?<high>\d+)\s*");
         if (match.Success)
         {
            var low = Convert.ToInt32(match.Groups["low"].Value);
            var high = Convert.ToInt32(match.Groups["high"].Value);
            var avg = (low + high) / 2;
            if (avg > Byte.MaxValue)
               avg = Byte.MaxValue;
            
            ret = Convert.ToByte(avg); //Return average of low and high
            return true;
         }

         //TODO: Error checking, large values, other unknown formats
         match = Regex.Match(servings, @"\d+"); // "Makes about 12 servings"
         return match.Success && Byte.TryParse(match.Value, out ret);
      }

      protected virtual byte ParseServings(HtmlNode document)
      {
         //Optional field - need to figure out what to do if this is not found (do we even want those recipes?)
         //parse "yield" class, which has no standard format.  AllRecipes says "6 to 8 servings", need to be able to parse various forms
         var yield = FindNodeByClass(document, "yield");
         if (yield == null) throw new MissingNodeException("No parsable yield");

         Byte ret;
         if (ParseRange(yield.InnerText, out ret))
            return ret;

         throw new MissingNodeException("No parsable yield");
      }

      protected virtual string ParseSteps(HtmlNode document)
      {
         var steps = FindNodeByClass(document, "instructions");
         if (steps == null)
            throw new MissingHRecipeTag();

         var html = steps.InnerHtml; //TODO: Clean up HTML?
         return html;
      }

      protected virtual string CleanHtml(string dirty)
      {
         var ret = Regex.Replace(dirty, @"[ ]?(id|class|href|src)\s*=\s*(""[^""]+""|'[^']+')", "");
         return ret;
      }

      protected virtual short? ParsePrepTime(HtmlNode document)
      {
         //TODO: Support non-valuetitle formats
         //TODO: Error checking

         var yield = FindNodeByClass(document, "preptime");
         if (yield == null)
            return null;

         var span = FindNodeByClass(yield, "value-title");

         var ret = ParseDuration(span.Attributes["title"].Value);
         return ret;
      }

      protected virtual short? ParseCookTime(HtmlNode document)
      {
         var yield = FindNodeByClass(document, "cooktime");
         if (yield == null)
            return null;

         var span = FindNodeByClass(yield, "value-title");

         var ret = ParseDuration(span.Attributes["title"].Value);
         return ret;
      }

      protected virtual string ParseDescription(HtmlNode document)
      {
         //Spec calls this "summary", as does Google and ZipList
         //AllRecipes has a summary div which plain text

         var yield = FindNodeByClass(document, "summary");
         return yield != null ? yield.InnerText.Trim() : null;
      }

      protected virtual byte ParseRating(HtmlNode document)
      {
         //No hRecipe spec for rating.  Google uses the hReview format, which has a "rating" tag - ZipList also uses "rating" (tricky to parse this)
         //AllRecipes puts the rating in an img title, such as "This recipe has an average star rating of 4.4" (Google seems to parse the numeric part)
         //Google can also parse "x/y" format such as "4/5" and normalize or "4.5" format, which is assumed to be out of 5

         var rating = FindNodeByClass(document, "rating");
         if (rating == null) return 0;

         var span = FindNodeByClass(rating, "value-title");
         if (span != null && span.Attributes.Contains("title"))
         {
            return ParseRating(span.Attributes["title"].Value);
         }

         return 0;
      }

      private byte ParseRating(string p)
      {
         Single ret;
         if (Single.TryParse(p, out ret)) //If it's just a number, parse that
         {
            return Convert.ToByte(ret);
         }

         var m = Regex.Match(p, @"^(?<num>\d+)/(?<denom>\d+)$");
         if (!m.Success) return 0;

         var num = Single.Parse(m.Groups["num"].Value);
         var denom = Single.Parse(m.Groups["denom"].Value);
         var rounded = Math.Round((num / denom) * 5);

         return Convert.ToByte(rounded);
      }

      protected virtual string ParsePhoto(HtmlNode document)
      {
         //hRecipe has no spec for photo.  ZipList and Google both use a "photo" class, which AllRecipes uses as well as an OpenGraph tag
         //AllRecipes has <img> with class="rec-image photo"

         var photo = FindNodeByClass(document, "photo");
         if (photo != null && photo.Name.Equals("img", StringComparison.InvariantCultureIgnoreCase))
         {
            return photo.Attributes["src"].Value;
         }

         return null;
      }

      public virtual String[] ParseIngredients(HtmlNode document)
      {
         //Spec says each ingredient has an "ingredient" class, and must include at least one
         //Most things will just use plain text, but spec also allows for value/type markup (we might not really care since we use NLP)

         var ret = FindNodesByClass(document, "ingredient");
         if (ret.Length <= 0) throw new MissingHRecipeTag();

         var list = ret.SelectMany(ing => ProcessIngredient(ing.InnerText)).ToArray();
         return list;
      }

      //Pre-process rogue ingredients, such as "salt and pepper"
      public static string[] ProcessIngredient(string ing)
      {
         var i = ing.ToLower().Trim();

         //TODO: We need a better system for these ingredients, preferably something that can be stored in the NLP database
         if (i.Equals("salt and pepper")) return new[] { "salt", "pepper" };
         if (i.Equals("salt & pepper")) return new[] { "salt", "pepper" };
         if (i.Equals("salt and black pepper")) return new[] { "salt", "black pepper" };
         if (i.Equals("salt & freshly ground black pepper")) return new[] { "salt", "freshly ground black pepper" };
         if (i.Equals("salt & freshly ground black pepper, to taste")) return new[] { "salt (to taste)", "freshly ground black pepper (to taste)" };
         if (i.Equals("salt and pepper to taste")) return new[] { "salt (to taste)", "pepper (to taste)" };
         if (i.Equals("salt and pepper, to taste")) return new[] { "salt (to taste)", "pepper (to taste)" };
         if (i.Equals("salt and pepper (to taste)")) return new[] { "salt (to taste)", "pepper (to taste)" };
         if (i.Equals("salt & pepper to taste")) return new[] { "salt (to taste)", "pepper (to taste)" };
         if (i.Equals("salt & pepper, to taste")) return new[] { "salt (to taste)", "pepper (to taste)" };
         if (i.Equals("salt & pepper (to taste)")) return new[] { "salt (to taste)", "pepper (to taste)" };
         if (i.Equals("kosher salt & freshly ground black pepper")) return new[] { "kosher salt", "freshly ground black pepper" };
         if (i.Equals("salt & fresh ground pepper")) return new[] { "salt", "fresh ground pepper" };

         return new[]
         {
            ing.Trim()
         };
      }

      public virtual string GetCredit(Uri page)
      {
         return page.Host;
      }

      protected virtual string PurifyIngredient(string ing)
      {
         var ret = Regex.Replace(ing, @"\s{2,}|\n", " ").Trim();
         ret = ret.Replace("&nbsp;", ""); //For some reason, AllRecipes has a lot of "empty" ingredients

         return ret;
      }

      protected virtual void BuildIngredients(Recipe recipe, IEnumerable<String> ingredients, out List<IngredientUsage> valid, out List<string> invalid)
      {
         valid = new List<IngredientUsage>();
         invalid = new List<string>();

         foreach (var ing in ingredients)
         {
            var safeIng = PurifyIngredient(ing);
            if (String.IsNullOrEmpty(safeIng))
               continue;

            var i = NlpParser.Parse(safeIng);

            if (i is NLP.Match)
            {
               valid.Add(i.Usage);
            }
            else
            {
               invalid.Add(safeIng);
            }
         }
      }

      public virtual ParserResult Parse(Uri page)
      {
         string[] ings;
         List<IngredientUsage> valid;
         List<String> missing;

         var result = new Recipe();
         var doc = new HtmlDocument();

         var client = new WebClient();
         var html = client.DownloadString(page);
         doc.LoadHtml(html);
         var root = doc.DocumentNode;

         try
         {
            result.Title = ParseTitle(root);
            result.Description = ParseDescription(root);
            result.ServingSize = ParseServings(root);
            result.PrepTime = ParsePrepTime(root);
            result.CookTime = ParseCookTime(root);
            result.Method = CleanHtml(ParseSteps(root));
            result.AvgRating = ParseRating(root);
            result.ImageUrl = ParsePhoto(root); //Parse fully qualified URL, we'll download a local copy just before DB save
            ings = ParseIngredients(root);
         }
         catch (MissingNodeException)
         {
            return new ParserResult(ParserResult.Status.MissingData);
         }
         catch (IndexingException)
         {
            return new ParserResult(ParserResult.Status.BadData);
         }
         
         BuildIngredients(result, ings, out valid, out missing);
         result.Ingredients = valid.ToArray();

         if (missing.Count > 0)
         {
            return new ParserResult(ParserResult.Status.UnknownIngredient)
            {
               UnknownIngredients = missing
            };
         }

         result.DateEntered = DateTime.Today;
         result.CreditUrl = page.ToString();
         result.Credit = GetCredit(page);

         return new ParserResult(result);
      }
   }

   [Parser("allrecipes.com")]
   class AllRecipesParser : hRecipeParser
   {
      protected override byte ParseServings(HtmlNode document)
      {
         //AllRecipes stores servings size in hidden input with the "servings-hdnservings" class
         var yield = FindNodeByClass(document, "servings-hdnservings");
         if (yield != null && yield.Attributes.Contains("value"))
         {
            byte ret;
            if (Byte.TryParse(yield.Attributes["value"].Value, out ret))
               return ret;
         }

         return base.ParseServings(document);
      }

      //AllRecipes uses "directions" instead of the standard "instructions"
      protected override string ParseSteps(HtmlNode document)
      {
         var steps = FindNodeByClass(document, "directions");
         if(steps == null)
            throw new MissingHRecipeTag();

         var html = steps.InnerHtml; //TODO: Clean up HTML?
         return html;
      }

      //AllRecipes puts the rating in a non-standard format on the title of an image
      protected override byte ParseRating(HtmlNode document)
      {
         var rating = FindNodeByClass(document, "rating");
         if (rating != null)
         {
            var title = rating.Attributes["title"].Value;
            var match = Regex.Match(title, @"\d.\d$"); //Match rating string (rating will appear at the end)
            if (match.Success)
            {
               float r;
               if (Single.TryParse(match.Value, out r))
               {
                  return Convert.ToByte(r); //Round to nearest byte
               }
            }
         }

         return 0;
      }

      public override string GetCredit(Uri page)
      {
         return "AllRecipes";
      }
   }

   [Parser("www.food.com")]
   class FoodDotComParser : hRecipeParser
   {
      //Food.com doesn't use hRecipe "servings" class anywhere
      protected override byte ParseServings(HtmlNode document)
      {
         //AllRecipes stores servings size in hidden input with the "servings-hdnservings" class
         var yield = FindNodeById(document, "original_value");
         if (yield != null && yield.Attributes.Contains("value"))
         {
            byte ret;
            if(ParseRange(yield.Attributes["value"].Value, out ret))
               return ret;
         }

         return base.ParseServings(document);
      }

      //Food.com uses the "ingredient" class twice, which results in duplicated ingredients
      public override string[] ParseIngredients(HtmlNode document)
      {
         var nodes = RecursiveCollect(document, n =>
         {
            return (n.Name == "li" && n.Attributes.Contains("class") && n.Attributes.Contains("itemprop") &&
               n.Attributes["class"].Value == "ingredient" && n.Attributes["itemprop"].Value == "ingredients");
         }).ToArray();

         if (nodes.Length > 0)
         {
            var list = nodes.SelectMany(ing => ProcessIngredient(ing.InnerText)).ToArray();
            return list;
         }

         throw new MissingHRecipeTag();
      }

      public override string GetCredit(Uri page)
      {
         return "Food.com";
      }

      protected override string ParseSteps(HtmlNode document)
      {
         var html = base.ParseSteps(document); //We need to parse out some markup
         html = Regex.Replace(html, @"\<div\s*class\=\""num\"">\s*\d+\s*\<\/div\>", "");

         return html;
      }
   }

   [Parser("www.epicurious.com")]
   class EpicuriousParser : hRecipeParser
   {
      public override string GetCredit(Uri page)
      {
         return "Epicurious";
      }

      protected override string ParseSteps(HtmlNode document)
      {
         var html = base.ParseSteps(document); //We need to parse out some markup
         html = Regex.Replace(html, @"\<a\s*href\=\"".+\""\>add your own note\<\/a>", "");

         return html;
      }
   }
}
