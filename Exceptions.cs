using System;

namespace KitchenPC.Parser
{
   class IndexingException : Exception
   {
      public IndexingException()
      {
      }

      public IndexingException(string msg) : base(msg)
      {
      }

      public IndexingException(string msg, Exception inner) : base(msg, inner)
      {
      }
   }

   class DuplicateParser : IndexingException
   {
      public DuplicateParser(string domain) : base("More than one class implements a parser for the domain: " + domain)
      {
      }
   }

   class MissingNodeException : IndexingException
   {
      public MissingNodeException(string message) : base(message)
      {
      }
   }

   class MissingHRecipeTag : IndexingException
   {
   }

   class UnknownIngredientException : IndexingException
   {
      public string Usage { get; private set; }
      public NLP.Result NlpResult { get; private set; }

      public UnknownIngredientException(string usage, NLP.Result result)
      {
         Usage = usage;
         NlpResult = result;
      }
   }

   class BadFormatException : IndexingException
   {
      public BadFormatException(string msg) : base(msg)
      {
      }
   }
}
