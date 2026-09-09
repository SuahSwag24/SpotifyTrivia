using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Services
{
    public class SpotifyApiResult<T>
    {
        public T? Data { get; set; }
        public string? RefreshedAccessToken { get; set; }
    }
}
