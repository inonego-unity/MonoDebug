using System;
using System.Collections;
using System.Collections.Generic;

using Xunit;

namespace MonoDebug.TEST
{
   // ============================================================
   /// <summary>
   /// Tests for ProfileCollection CRUD and ID management.
   /// </summary>
   // ============================================================
   public class TEST_ProfileCollection
   {

   #region Constructor

      [Fact]
      public void Constructor_CreatesDefaultProfile()
      {
         var profiles = new ProfileCollection(null);

         Assert.True(profiles.Exists("default"));
      }

      [Fact]
      public void Constructor_DefaultIsActive()
      {
         var profiles = new ProfileCollection(null);

         Assert.True(profiles.IsActive("default"));
      }

   #endregion

   #region Create

      [Fact]
      public void Create_NewProfile()
      {
         var profiles = new ProfileCollection(null);

         var prof = profiles.Create("auth", "Auth debugging");

         Assert.NotNull(prof);
         Assert.Equal("auth", prof.Name);
         Assert.Equal("Auth debugging", prof.Desc);
      }

      [Fact]
      public void Create_Duplicate_ReturnsNull()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("auth");

         Assert.Null(profiles.Create("auth"));
      }

   #endregion

   #region Remove

      [Fact]
      public void Remove_ExistingProfile()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("temp");

         Assert.True(profiles.Remove("temp"));
         Assert.False(profiles.Exists("temp"));
      }

      [Fact]
      public void Remove_NonExistent_ReturnsFalse()
      {
         var profiles = new ProfileCollection(null);

         Assert.False(profiles.Remove("ghost"));
      }

   #endregion

   #region Get / List / Exists

      [Fact]
      public void Get_ReturnsProfile()
      {
         var profiles = new ProfileCollection(null);

         var def = profiles.Get("default");

         Assert.NotNull(def);
         Assert.Equal("default", def.Name);
      }

      [Fact]
      public void Get_NonExistent_ReturnsNull()
      {
         var profiles = new ProfileCollection(null);

         Assert.Null(profiles.Get("ghost"));
      }

      [Fact]
      public void List_ReturnsAll()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("a");
         profiles.Create("b");

         var list = profiles.List();

         Assert.Equal(3, list.Count); // default + a + b
      }

   #endregion

   #region Switch / Enable / Disable

      [Fact]
      public void Switch_ActivatesOnly()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("auth");
         profiles.Switch("auth");

         Assert.True(profiles.IsActive("auth"));
         Assert.False(profiles.IsActive("default"));
      }

      [Fact]
      public void Enable_MakesActive()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("auth");
         profiles.Enable("auth");

         Assert.True(profiles.IsActive("auth"));
         Assert.True(profiles.IsActive("default")); // still active
      }

      [Fact]
      public void Disable_MakesInactive()
      {
         var profiles = new ProfileCollection(null);

         profiles.Disable("default");

         Assert.False(profiles.IsActive("default"));
      }

      [Fact]
      public void DisableAll()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("auth");
         profiles.Enable("auth");

         profiles.DisableAll();

         Assert.False(profiles.IsActive("default"));
         Assert.False(profiles.IsActive("auth"));
      }

   #endregion

   #region NextId

      [Fact]
      public void NextId_Increments()
      {
         var profiles = new ProfileCollection(null);

         int a = profiles.NextId();
         int b = profiles.NextId();
         int c = profiles.NextId();

         Assert.Equal(a + 1, b);
         Assert.Equal(b + 1, c);
      }

      [Fact]
      public void NextId_UniqueAcrossProfiles()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("auth");

         int id1 = profiles.NextId(); // used by default
         int id2 = profiles.NextId(); // used by auth

         Assert.NotEqual(id1, id2);
      }

   #endregion

   #region Edit

      [Fact]
      public void Edit_ChangeDesc()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("auth", "old");

         var edited = profiles.Edit("auth", "new desc");

         Assert.Equal("new desc", edited.Desc);
      }

      [Fact]
      public void Edit_Rename()
      {
         var profiles = new ProfileCollection(null);

         profiles.Create("old");

         var renamed = profiles.Edit("old", newName: "new");

         Assert.NotNull(renamed);
         Assert.Equal("new", renamed.Name);
         Assert.False(profiles.Exists("old"));
         Assert.True(profiles.Exists("new"));
      }

   #endregion

   }
}
