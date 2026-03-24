using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

using Xunit;

namespace MonoDebug.TEST
{
   // ============================================================
   /// <summary>
   /// Tests for OptionExtensions and Args helpers.
   /// </summary>
   // ============================================================
   public class TEST_OptionExtensions
   {

   #region Has

      [Fact]
      public void Has_Exists()
      {
         var opts = new Dictionary<string, JsonElement>
         {
            ["full"] = JsonDocument.Parse("true").RootElement
         };

         Assert.True(opts.Has("full"));
      }

      [Fact]
      public void Has_Missing()
      {
         var opts = new Dictionary<string, JsonElement>();

         Assert.False(opts.Has("full"));
      }

   #endregion

   #region GetString

      [Fact]
      public void GetString_ReturnsValue()
      {
         var opts = new Dictionary<string, JsonElement>
         {
            ["name"] = JsonDocument.Parse("\"hello\"").RootElement
         };

         Assert.Equal("hello", opts.GetString("name"));
      }

      [Fact]
      public void GetString_Missing_ReturnsFallback()
      {
         var opts = new Dictionary<string, JsonElement>();

         Assert.Null(opts.GetString("name"));
         Assert.Equal("default", opts.GetString("name", "default"));
      }

   #endregion

   #region GetInt

      [Fact]
      public void GetInt_ReturnsValue()
      {
         var opts = new Dictionary<string, JsonElement>
         {
            ["timeout"] = JsonDocument.Parse("5000").RootElement
         };

         Assert.Equal(5000, opts.GetInt("timeout"));
      }

      [Fact]
      public void GetInt_Missing_ReturnsFallback()
      {
         var opts = new Dictionary<string, JsonElement>();

         Assert.Equal(30, opts.GetInt("timeout", 30));
      }

      [Fact]
      public void GetInt_StringValue()
      {
         var opts = new Dictionary<string, JsonElement>
         {
            ["timeout"] = JsonDocument.Parse("\"5000\"").RootElement
         };

         Assert.Equal(5000, opts.GetInt("timeout"));
      }

   #endregion

   #region GetLong

      [Fact]
      public void GetLong_ReturnsValue()
      {
         var opts = new Dictionary<string, JsonElement>
         {
            ["thread"] = JsonDocument.Parse("42").RootElement
         };

         Assert.Equal(42L, opts.GetLong("thread"));
      }

   #endregion

   #region GetBool

      [Fact]
      public void GetBool_True()
      {
         var opts = new Dictionary<string, JsonElement>
         {
            ["verbose"] = JsonDocument.Parse("true").RootElement
         };

         Assert.True(opts.GetBool("verbose"));
      }

      [Fact]
      public void GetBool_Missing_ReturnsFallback()
      {
         var opts = new Dictionary<string, JsonElement>();

         Assert.False(opts.GetBool("verbose"));
      }

   #endregion

   #region TryParseId

      [Fact]
      public void TryParseId_Valid()
      {
         var args = new List<string> { "42" };

         Assert.True(args.TryParseId(out int id));
         Assert.Equal(42, id);
      }

      [Fact]
      public void TryParseId_Invalid()
      {
         var args = new List<string> { "abc" };

         Assert.False(args.TryParseId(out _));
      }

      [Fact]
      public void TryParseId_Empty()
      {
         var args = new List<string>();

         Assert.False(args.TryParseId(out _));
      }

   #endregion

   }
}
