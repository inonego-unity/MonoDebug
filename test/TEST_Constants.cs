using System;

using Xunit;

namespace MonoDebug.TEST
{
   // ============================================================
   /// <summary>
   /// Tests for Constants.
   /// </summary>
   // ============================================================
   public class TEST_Constants
   {

   #region Values

      [Fact]
      public void PipePrefix_NotEmpty()
      {
         Assert.False(string.IsNullOrEmpty(Constants.PipePrefix));
      }

      [Fact]
      public void Timeouts_Positive()
      {
         Assert.True(Constants.DaemonTimeout > 0);
         Assert.True(Constants.ConnectTimeout > 0);
      }

   #endregion

   #region Error Codes

      [Fact]
      public void ErrorCodes_NotEmpty()
      {
         Assert.False(string.IsNullOrEmpty(Constants.Error.NoSession));
         Assert.False(string.IsNullOrEmpty(Constants.Error.NotStopped));
         Assert.False(string.IsNullOrEmpty(Constants.Error.InvalidArgs));
         Assert.False(string.IsNullOrEmpty(Constants.Error.ConnectFailed));
         Assert.False(string.IsNullOrEmpty(Constants.Error.EvalError));
         Assert.False(string.IsNullOrEmpty(Constants.Error.Timeout));
         Assert.False(string.IsNullOrEmpty(Constants.Error.AlreadyExists));
         Assert.False(string.IsNullOrEmpty(Constants.Error.NotFound));
      }

   #endregion

   }
}
