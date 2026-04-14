namespace APM.StaffZen.API.Services
{
    using System.Security.Cryptography;
    using System.Text;

    public class PasswordService
    {
        public void CreatePasswordHash(string password, out string hash, out string salt)
        {
            using var hmac = new HMACSHA256();
            salt = Convert.ToBase64String(hmac.Key);
            hash = Convert.ToBase64String(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(password)));
        }
        public bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            // Check for null values
            if (string.IsNullOrEmpty(password) || 
                string.IsNullOrEmpty(storedHash) || 
                string.IsNullOrEmpty(storedSalt))
            {
                return false;
            }

            var key = Convert.FromBase64String(storedSalt);

            using var hmac = new HMACSHA256(key);

            var computedHash = Convert.ToBase64String(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(password)));

            return computedHash == storedHash;
        }
    }

}
