namespace ForgeScope.Hub;

public static partial class Views
{
    static void AccountInbox(Page p, GitHubInbox inbox)
    {
        var b = p.Body;
        b.Append($"<section id=\"github-inbox\" class=\"card\"><h2>{p.T("Your GitHub notifications", "Suas notificações do GitHub", "Tus notificaciones de GitHub")}</h2><p>{p.T("Issues, pull requests, mentions, reviews and activity returned by GitHub for the account signed in here. This is separate from repository CI monitoring below.", "Issues, pull requests, menções, revisões e atividades retornadas pelo GitHub para a conta conectada aqui. Abaixo fica o monitoramento de CI dos repositórios.", "Issues, pull requests, menciones, revisiones y actividad de la cuenta conectada. Más abajo está el monitoreo de CI.")}</p>");
        if (inbox.Error.Length > 0)
        {
            b.Append($"<p role=\"status\">{(inbox.Error == "permission" ? p.T("Your current session cannot read GitHub notifications. Authorize the notifications permission to connect your inbox; access is not granted by signing in alone.", "Sua sessão atual não pode ler as notificações do GitHub. Autorize a permissão notifications para conectar sua caixa; entrar com GitHub, sozinho, não concede esse acesso.", "Tu sesión no puede leer las notificaciones. Autoriza el permiso notifications para conectar tu bandeja.") : p.T("GitHub notifications are temporarily unavailable. Refresh later; repository monitoring remains available below.", "As notificações do GitHub estão temporariamente indisponíveis. Atualize mais tarde; o monitoramento de repositórios continua disponível abaixo.", "Las notificaciones no están disponibles temporalmente. Actualiza más tarde."))}</p>");
            if (inbox.Error == "permission") b.Append($"<a class=\"button primary\" href=\"/auth/github/notifications?lang={p.Lang}\">{p.T("Authorize GitHub notifications", "Autorizar notificações do GitHub", "Autorizar notificaciones de GitHub")}</a><p class=\"muted\">{p.T("GitHub will show the additional permission before you accept. ForgeScope only reads this inbox; opening this page does not mark GitHub threads as read.", "O GitHub mostrará a permissão adicional antes de você aceitar. O ForgeScope apenas lê esta caixa; abrir esta página não marca tópicos como lidos no GitHub.", "GitHub mostrará el permiso antes de aceptar. Abrir esta página no marca los hilos como leídos.")}</p>");
        }
        else
        {
            b.Append($"<p class=\"muted\">{p.T("50 items per page, including read and unread items still retained by GitHub. PR state is checked for up to 10 pull requests per page. This is not a list of every commit or merge: GitHub notification subscriptions determine which events appear.", "50 itens por página, incluindo lidos e não lidos ainda retidos pelo GitHub. O estado é consultado para até 10 PRs por página. Esta não é uma lista de todos os commits ou merges: suas assinaturas de notificações no GitHub determinam os eventos exibidos.", "50 elementos por página, incluidos leídos y no leídos retenidos por GitHub. Se consulta el estado de hasta 10 PRs por página. Las suscripciones determinan los eventos mostrados.")}</p>");
            if (inbox.Items.Count == 0) b.Append($"<p>{p.T("GitHub returned no notifications on this page.", "O GitHub não retornou notificações nesta página.", "GitHub no devolvió notificaciones en esta página.")}</p>");
            foreach (var n in inbox.Items)
            {
                string kind = n.Type switch { "PullRequest" => p.T("Pull request", "Pull request", "Pull request"), "Issue" => "Issue", "CheckSuite" => p.T("Checks / CI", "Verificações / CI", "Verificaciones / CI"), "Discussion" => p.T("Discussion", "Discussão", "Discusión"), "Commit" => "Commit", "Release" => p.T("Release", "Versão publicada", "Versión publicada"), _ => p.T("Activity", "Atividade", "Actividad") };
                string state = n.State switch { "merged" => p.T("Merged", "Merge realizado", "Fusionado"), "closed" => p.T("Closed", "Fechado", "Cerrado"), "open" => p.T("Open", "Aberto", "Abierto"), _ => "" };
                string reason = n.Reason switch { "mention" => p.T("You were mentioned", "Você foi mencionado", "Te mencionaron"), "review_requested" => p.T("Review requested", "Revisão solicitada", "Revisión solicitada"), "author" => p.T("Your thread", "Tópico criado por você", "Tu hilo"), "ci_activity" => p.T("CI activity", "Atividade de CI", "Actividad de CI"), "subscribed" => p.T("Subscribed", "Você acompanha", "Suscrito"), "comment" => p.T("Commented", "Você comentou", "Comentado"), "state_change" => p.T("State changed", "Estado alterado", "Estado cambiado"), "assign" => p.T("Assigned", "Atribuído a você", "Asignado"), "team_mention" => p.T("Team mentioned", "Equipe mencionada", "Equipo mencionado"), _ => p.T("Notification", "Notificação", "Notificación") };
                b.Append($"<article class=\"notice-row\"><span class=\"badge\">{kind}</span> <span class=\"badge\">{(n.Unread ? p.T("Unread", "Não lida", "No leída") : p.T("Read", "Lida", "Leída"))}</span> {(state.Length > 0 ? $"<span class=\"badge\">{state}</span>" : "")}<h3><a href=\"{H(n.Url)}\" target=\"_blank\" rel=\"noopener noreferrer\">{H(n.Title)}</a></h3><p>{H(n.Repository)} · {reason}</p><small>{H(n.Updated)}</small></article>");
            }
            b.Append($"<nav class=\"actions\" aria-label=\"{p.T("GitHub notification pages", "Páginas das notificações do GitHub", "Páginas de notificaciones de GitHub")}\">");
            if (inbox.Page > 1) b.Append($"<a href=\"/notifications?lang={p.Lang}&amp;ghpage={inbox.Page-1}#github-inbox\">{p.T("Previous page", "Página anterior", "Página anterior")}</a>");
            b.Append($"<span>{p.T("Page", "Página", "Página")} {inbox.Page}</span>");
            if (inbox.More) b.Append($"<a href=\"/notifications?lang={p.Lang}&amp;ghpage={inbox.Page+1}#github-inbox\">{p.T("Next page", "Próxima página", "Página siguiente")}</a>");
            b.Append($"</nav><a class=\"button\" href=\"/notifications?lang={p.Lang}&amp;ghpage={inbox.Page}\">{p.T("Refresh GitHub inbox", "Atualizar caixa do GitHub", "Actualizar bandeja de GitHub")}</a>");
        }
        b.Append("</section>");
    }
}
