using Blazored.LocalStorage;
using InvestmentTracker.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net;
using System.Net.Http.Headers;

namespace InvestmentTracker.Client.Handlers
{
    public class AuthHandler : DelegatingHandler
    {
        private readonly ILocalStorageService _localStorage;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly NavigationManager _navigation;

        public AuthHandler(
            ILocalStorageService localStorage,
            AuthenticationStateProvider authStateProvider,
            NavigationManager navigation)
        {
            _localStorage = localStorage;
            _authStateProvider = authStateProvider;
            _navigation = navigation;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Добавляем токен, если есть
            var token = await _localStorage.GetItemAsync<string>("authToken");
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            // Отправляем запрос
            var response = await base.SendAsync(request, cancellationToken);

            // Если сервер вернул 401 (Unauthorized) — токен недействителен
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Очищаем токен
                await _localStorage.RemoveItemAsync("authToken");

                // Уведомляем Blazor, что пользователь больше не авторизован
                if (_authStateProvider is CustomAuthStateProvider customAuth)
                {
                    customAuth.NotifyUserLogout();
                }

                // Перенаправляем на страницу входа (только если мы не уже на ней)
                if (!_navigation.Uri.EndsWith("/login"))
                {
                    _navigation.NavigateTo("/login", forceLoad: true);
                }
            }

            return response;
        }
    }
}