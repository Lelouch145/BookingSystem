local finalizingKey = redis.call('GET', KEYS[#KEYS])
local success, lockData = pcall(cjson.decode, finalizingKey)
local holdId = redis.call('GET', KEYS[1])
local userHoldId = ARGV[1]
local holdData = redis.call('GET', KEYS[2])
local holdDataValue = ARGV[2]
local lockToken = ARGV[3]
local slotStart = 3
local slotEnd = #KEYS - 1
if not success or type(lockData) ~= 'table' then
    return 11
end

if lockData.lockToken ~= lockToken then
    return 10
end

if holdId ~= userHoldId then
    return 2
end

if holdData ~= holdDataValue then
    return 3
end

for i = slotStart, slotEnd do
    local slot = redis.call('GET', KEYS[i])
    if slot ~= userHoldId then
        return 6
    end
end

local originalExpireAtMs = lockData.originalExpireAtMs
local currentTime = redis.call('TIME')

local currentTimeMs = (tonumber(currentTime[1]) * 1000) + math.floor(tonumber(currentTime[2]) / 1000)

if originalExpireAtMs <= currentTimeMs then
    for i = 1, #KEYS do
        redis.call('DEL', KEYS[i])
    end
    return 1
end


for i = 1, #KEYS - 1 do
    redis.call('PEXPIREAT', KEYS[i], originalExpireAtMs)
end
redis.call('DEL', KEYS[#KEYS])

return 1
