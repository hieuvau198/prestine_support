﻿using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.DTOs;
using Application.DTOs.Auth;
using Application.Interfaces;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IConfiguration _configuration;
        private readonly IUserService _userService;

        public AuthService(
            IUserRepository userRepository,
            IConfiguration configuration,
            IUserService userService)
        {
            _userRepository = userRepository;
            _configuration = configuration;
            _userService = userService;
            var token = new JwtSecurityToken();
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            var user = await _userRepository.GetByUsernameAsync(loginDto.Username);
            if (user == null || !VerifyPassword(loginDto.Password, user.Password))
                throw new UnauthorizedAccessException("Invalid username or password");

            // Update last login time
            user.LastLoginAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);

            var (accessToken, accessTokenExpiration) = GenerateAccessToken(user);
            var refreshToken = GenerateRefreshToken();

            // Store refresh token (you might want to create a separate repository for this)
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await _userRepository.UpdateAsync(user);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiration = accessTokenExpiration,
                User = _userService.MapToDto(user)
            };
        }

        public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenDto refreshTokenDto)
        {
            var principal = GetPrincipalFromExpiredToken(refreshTokenDto.AccessToken);
            var username = principal.Identity?.Name;

            if (username == null)
                throw new UnauthorizedAccessException("Invalid access token");

            var user = await _userRepository.GetByUsernameAsync(username);
            if (user == null ||
                user.RefreshToken != refreshTokenDto.RefreshToken ||
                user.RefreshTokenExpiryTime <= DateTime.UtcNow)
                throw new UnauthorizedAccessException("Invalid refresh token");

            var (newAccessToken, accessTokenExpiration) = GenerateAccessToken(user);
            var newRefreshToken = GenerateRefreshToken();

            // Update refresh token
            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await _userRepository.UpdateAsync(user);

            return new AuthResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                AccessTokenExpiration = accessTokenExpiration,
                User = _userService.MapToDto(user)
            };
        }

        public async Task LogoutAsync(string username)
        {
            var user = await _userRepository.GetByUsernameAsync(username);
            if (user != null)
            {
                // Invalidate refresh token
                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;
                await _userRepository.UpdateAsync(user);
            }
        }

        private (string Token, DateTime Expiration) GenerateAccessToken(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role ?? "User")
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                _configuration["Jwt:SecretKey"] ??
                throw new InvalidOperationException("JWT Secret Key is not configured")));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expiration = DateTime.UtcNow.AddMinutes(
                double.Parse(_configuration["Jwt:AccessTokenExpirationMinutes"] ?? "30"));

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expiration,
                signingCredentials: creds
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), expiration);
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }

        private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"] ??
                    throw new InvalidOperationException("JWT Secret Key is not configured"))),
                ValidateLifetime = false, // We want to validate the token, even if it's expired
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidAudience = _configuration["Jwt:Audience"]
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
            return principal;
        }

        private bool VerifyPassword(string inputPassword, string storedHash)
        {
            return BCrypt.Net.BCrypt.Verify(inputPassword, storedHash);
        }
    }
}




//using System;
//using System.Threading.Tasks;
//using Application.DTOs;
//using Application.DTOs.Auth;
//using Application.Interfaces;

//namespace Application.Services
//{
//    public class AuthService : IAuthService
//    {
//        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
//        {
//            // Empty response
//            return await Task.FromResult<AuthResponseDto>(null);
//        }

//        public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenDto refreshTokenDto)
//        {
//            // Empty response
//            return await Task.FromResult<AuthResponseDto>(null);
//        }

//        public async Task LogoutAsync(string username)
//        {
//            // Do nothing
//            await Task.CompletedTask;
//        }

//        private (string Token, DateTime Expiration) GenerateAccessToken()
//        {
//            // No token generation, empty response
//            return (string.Empty, DateTime.MinValue);
//        }

//        private string GenerateRefreshToken()
//        {
//            // No refresh token generation, empty response
//            return string.Empty;
//        }

//        private bool VerifyPassword(string inputPassword, string storedHash)
//        {
//            // Always return false for password verification
//            return false;
//        }
//    }
//}
