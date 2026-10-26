local holdId = redis.call('GET', KEYS[1])
local holdData = redis.call('GET', KEYS[2])
local slotKeyStartIndex = 3

if holdId ~= ARGV[1] then 
    return 2
end

if holdData ~= ARGV[2] then
    return 3
end

for i = slotKeyStartIndex, #KEYS - 1 do
    local slotHoldId = redis.call('GET', KEYS[i])
    
    if slotHoldId ~= ARGV[1] then
        return 6
    end
end

if redis.call('EXISTS', KEYS[#KEYS]) == 1 then
    return 9
end

local expiresAtMs = redis.call('PEXPIRETIME', KEYS[2])

if expiresAtMs <= 0 then
    return 5
end

local time = redis.call('TIME')
local nowMs = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

local expireLimit = math.max(expiresAtMs, nowMs + 60000)

redis.call('SET', KEYS[#KEYS], ARGV[3], 'PXAT', expireLimit)

for i = 1, #KEYS - 1 do
    redis.call('PEXPIREAT', KEYS[i], expireLimit)
end

return 1





