using CINE.Translate;

namespace Demo;

/// <summary>
/// The settings screen of the demo notes app. Every visible text goes through Rcinel.Translate:
/// the English text is written right here, and `rcinel scan --assign` gave each call its id.
/// </summary>
public sealed class SettingsPage
{
  public string Title => Rcinel.Translate("01a192e3-5bcc-7000-a8d2-327b1cb0f737", "Settings");
  public string Appearance => Rcinel.Translate("01a192e3-5bd3-7000-a5f3-da9468077405", "Appearance");
  public string DarkMode => Rcinel.Translate("01a192e3-5bd4-7000-8e32-166cb3e992e1", "Dark mode");
  public string DarkModeHint => Rcinel.Translate("01a192e3-5bd4-7001-9444-865b7a85eec0", "Easier on the eyes at night");
  public string Language => Rcinel.Translate("01a192e3-5bd4-7002-8729-40f06f1ec387", "Language");
  public string Notifications => Rcinel.Translate("01a192e3-5bd4-7003-8ffa-11184e6d893e", "Notifications");
  public string EmailMe => Rcinel.Translate("01a192e3-5bd4-7004-9bce-eb810c64ab20", "Email me when someone shares a note with me");
  public string Save => Rcinel.Translate("01a192e3-5bd4-7005-b3d0-b98f0a9cfe3d", "Save");
  public string Cancel => Rcinel.Translate("01a192e3-5bd4-7006-8fab-925a6177e082", "Cancel");

  // Numbered placeholders are filled by the overload, named ones by the app.
  public string Saved(int count) => Rcinel.Translate("01a192e3-5bd4-7007-bb84-212f497c373e", "Saved {0} settings", count);
  public string Welcome(string name) => Rcinel.Translate("01a192e3-5bd4-7008-9a54-036fafe0c7af", "Welcome back, {name}!").Replace("{name}", name);
}
