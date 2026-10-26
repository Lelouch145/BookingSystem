local finalizingKey = redis.call('GET', KEYS[#KEYS])
local lockToken = ARGV[3]
local holdId = redis.call('GET', KEYS[1])
local userHoldId = ARGV[1]
local holdData = redis.call('GET', KEYS[2])
local userHoldData = ARGV[2]
local slotStart = 3
local slotEnd = #KEYS - 1

if finalizingKey ~= lockToken then
    return 10
end

if holdId ~= userHoldId then
    return 2
end

if holdData ~= userHoldData then
    return 3
end

for i = slotStart, slotEnd do
    local slot = redis.call('GET', KEYS[i])
    if slot ~= userHoldId then
        return 6
    end
end

for i = 1, #KEYS do
    redis.call('DEL', KEYS[i])
end

return 1


