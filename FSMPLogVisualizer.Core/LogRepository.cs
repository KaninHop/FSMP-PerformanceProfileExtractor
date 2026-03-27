using SQLite;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FSMPLogVisualizer.Core
{
    public class LogRepository
    {
        private SQLiteAsyncConnection _database;

        public LogRepository(string dbPath)
        {
            _database = new SQLiteAsyncConnection(dbPath);
            _database.CreateTableAsync<LogRunSession>().Wait();
            _database.CreateTableAsync<LogDataPoint>().Wait();
        }

        public async Task<int> SaveSessionAsync(LogRunSession session)
        {
            if (session.Id != 0)
            {
                return await _database.UpdateAsync(session);
            }
            else
            {
                return await _database.InsertAsync(session);
            }
        }

        public async Task<int> SaveDataPointsAsync(IEnumerable<LogDataPoint> points)
        {
            return await _database.InsertAllAsync(points);
        }

        public async Task<List<LogRunSession>> GetSessionsAsync()
        {
            return await _database.Table<LogRunSession>().ToListAsync();
        }

        public async Task<List<LogDataPoint>> GetDataPointsAsync(int sessionId)
        {
            return await _database.Table<LogDataPoint>().Where(p => p.SessionId == sessionId).ToListAsync();
        }

        public async Task<List<LogDataPoint>> GetAllDataPointsAsync()
        {
            return await _database.Table<LogDataPoint>().ToListAsync();
        }

        public async Task<bool> SessionExistsAsync(string sessionKey)
        {
            var existing = await _database.Table<LogRunSession>().Where(s => s.SessionKey == sessionKey).FirstOrDefaultAsync();
            return existing != null;
        }

        public async Task ClearAllAsync()
        {
            await _database.DeleteAllAsync<LogDataPoint>();
            await _database.DeleteAllAsync<LogRunSession>();
        }
    }
}
