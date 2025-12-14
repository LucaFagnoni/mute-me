using Avalonia.Controls;
using System;

namespace Mute_Me;

public partial class HotkeyPopup : Window
{
    // Evento per avvisare la MainWindow se l'utente annulla (clicca fuori)
    public event Action? Canceled;

    public HotkeyPopup()
    {
        InitializeComponent();
        
        // Se l'utente clicca fuori dalla finestra, annulliamo la registrazione
        this.Deactivated += (s, e) => 
        {
            Canceled?.Invoke();
            this.Close();
        };
    }
}
