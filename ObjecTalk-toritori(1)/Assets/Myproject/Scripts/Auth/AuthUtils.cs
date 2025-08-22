using UnityEngine;

public static class AuthUtils
{
    public static bool IsValidEmail(string email)
    {
        return !string.IsNullOrEmpty(email) && email.Contains("@") && email.Contains(".");
    }

    public static string ParseErrorMessage(string originalMessage)
    {
        if (originalMessage.Contains("weak-password"))
            return "パスワードが弱すぎます。\n6文字以上で設定してください。";
        else if (originalMessage.Contains("invalid-email"))
            return "メールアドレスの形式が正しくありません。";
        else if (originalMessage.Contains("email-already-in-use"))
            return "このメールアドレスは既に使用されています。";
        else if (originalMessage.Contains("wrong-password") || originalMessage.Contains("invalid-credential"))
            return "メールアドレスまたはパスワードが正しくありません。";
        else if (originalMessage.Contains("user-not-found"))
            return "このメールアドレスのアカウントが見つかりません。";
        else if (originalMessage.Contains("too-many-requests"))
            return "リクエストが多すぎます。しばらくお待ちください。";
        else
            return "メールアドレスまたはパスワードが違います。";
    }
}