
    -- User already has an active hold
    if redis.call('EXISTS', KEYS[1]) == 1 then
        return {7, 0}
    end

    -- HoldId already exists
    if redis.call('EXISTS', KEYS[2]) == 1 then
        return {8, 0}
    end 
    -- Slots are already reserved
    for i = 3, #KEYS do
        if redis.call('EXISTS', KEYS[i]) == 1 then
            return {4, 0}
        end
    end

    -- Save user's HoldId
    redis.call('SET', KEYS[1], ARGV[1], 'EX', 300)

    -- Save reservation information
    redis.call('SET', KEYS[2], ARGV[2], 'EX', 300)

    -- Reserve booking slots
    for i = 3, #KEYS do
        redis.call('SET', KEYS[i], ARGV[1], 'EX', 300)
    end

    return {1, redis.call('EXPIRETIME', KEYS[2])}

