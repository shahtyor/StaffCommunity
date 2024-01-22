namespace StaffCommunity
{
    partial class StaffCommunityBot
    {
        /// <summary> 
        /// Обязательная переменная конструктора.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private static System.Diagnostics.EventLog eventLogBot;

        /// <summary>
        /// Освободить все используемые ресурсы.
        /// </summary>
        /// <param name="disposing">истинно, если управляемый ресурс должен быть удален; иначе ложно.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }


        #region Код, автоматически созданный конструктором компонентов

        /// <summary> 
        /// Требуемый метод для поддержки конструктора — не изменяйте 
        /// содержимое этого метода с помощью редактора кода.
        /// </summary>
        private void InitializeComponent()
        {
            eventLogBot = new System.Diagnostics.EventLog();
            ((System.ComponentModel.ISupportInitialize)(eventLogBot)).BeginInit();

            components = new System.ComponentModel.Container();
            this.ServiceName = "StaffCommunityBot";

            ((System.ComponentModel.ISupportInitialize)(eventLogBot)).EndInit();
        }

        #endregion
    }
}
