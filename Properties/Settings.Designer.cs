﻿//------------------------------------------------------------------------------
// <auto-generated>
//     Этот код создан программой.
//     Исполняемая версия:4.0.30319.42000
//
//     Изменения в этом файле могут привести к неправильной работе и будут потеряны в случае
//     повторной генерации кода.
// </auto-generated>
//------------------------------------------------------------------------------

namespace StaffCommunity.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.11.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Server = localhost; Port = 5432; User Id = postgres; Password = e4r5t6; Database " +
            "= sae")]
        public string ConnectionString {
            get {
                return ((string)(this["ConnectionString"]));
            }
            set {
                this["ConnectionString"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("6457417713:AAFrqt3BSYdQy3-w73SAXKvrMXGy8btoJ0E")]
        public string BotToken {
            get {
                return ((string)(this["BotToken"]));
            }
            set {
                this["BotToken"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public int TimeoutProcess {
            get {
                return ((int)(this["TimeoutProcess"]));
            }
            set {
                this["TimeoutProcess"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("15")]
        public int TimeoutVoid {
            get {
                return ((int)(this["TimeoutVoid"]));
            }
            set {
                this["TimeoutVoid"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("6906986784:AAGWHhFXFQ3YVyu_c0fdJ1v13Pwsn5nbmBg")]
        public string BotSearchToken {
            get {
                return ((string)(this["BotSearchToken"]));
            }
            set {
                this["BotSearchToken"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool AgentControl {
            get {
                return ((bool)(this["AgentControl"]));
            }
            set {
                this["AgentControl"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("2")]
        public int TimeoutReady {
            get {
                return ((int)(this["TimeoutReady"]));
            }
            set {
                this["TimeoutReady"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int TokensFor_1_week_sub {
            get {
                return ((int)(this["TokensFor_1_week_sub"]));
            }
            set {
                this["TokensFor_1_week_sub"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("20")]
        public int TokensFor_1_month_sub {
            get {
                return ((int)(this["TokensFor_1_month_sub"]));
            }
            set {
                this["TokensFor_1_month_sub"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public int TokensFor_3_day_sub {
            get {
                return ((int)(this["TokensFor_3_day_sub"]));
            }
            set {
                this["TokensFor_3_day_sub"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool VerifyEmail {
            get {
                return ((bool)(this["VerifyEmail"]));
            }
            set {
                this["VerifyEmail"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("3")]
        public int Timeout_D1 {
            get {
                return ((int)(this["Timeout_D1"]));
            }
            set {
                this["Timeout_D1"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1")]
        public int Timeout_T1 {
            get {
                return ((int)(this["Timeout_T1"]));
            }
            set {
                this["Timeout_T1"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("24")]
        public int Timeout_D2 {
            get {
                return ((int)(this["Timeout_D2"]));
            }
            set {
                this["Timeout_D2"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("2")]
        public int Timeout_T2 {
            get {
                return ((int)(this["Timeout_T2"]));
            }
            set {
                this["Timeout_T2"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("4")]
        public int Timeout_T3 {
            get {
                return ((int)(this["Timeout_T3"]));
            }
            set {
                this["Timeout_T3"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("http://localhost:8880/api")]
        public string UrlApi {
            get {
                return ((string)(this["UrlApi"]));
            }
            set {
                this["UrlApi"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("F:\\SAEwork\\FIREBASE\\safire.json")]
        public string FirebaseJson {
            get {
                return ((string)(this["FirebaseJson"]));
            }
            set {
                this["FirebaseJson"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("64fdg9@w")]
        public string ServiceKey {
            get {
                return ((string)(this["ServiceKey"]));
            }
            set {
                this["ServiceKey"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool PushAgent {
            get {
                return ((bool)(this["PushAgent"]));
            }
            set {
                this["PushAgent"] = value;
            }
        }
    }
}
