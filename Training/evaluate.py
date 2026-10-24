"""Held-out evaluation through the actual Unity physics executable, no planner."""
import argparse
import json
from collections import defaultdict
from pathlib import Path
import numpy as np
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--env', required=True)
    parser.add_argument('--model')
    parser.add_argument('--level', type=int, default=1)
    parser.add_argument('--stage', type=int, choices=range(5))
    parser.add_argument('--seed', type=int, default=900001)
    parser.add_argument('--episodes', type=int, default=128)
    parser.add_argument('--worker-id', type=int, default=7)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    engine = EngineConfigurationChannel()
    engine.set_configuration_parameters(time_scale=20, quality_level=0)
    params = EnvironmentParametersChannel()
    params.set_float_parameter('level', args.level)
    params.set_float_parameter('seed', args.seed)
    if args.stage is not None:
        params.set_float_parameter('stage', args.stage)
        params.set_float_parameter('simple_fraction', 0)
    stats = StatsSideChannel()
    model = None
    if args.model:
        import onnxruntime as ort
        model = ort.InferenceSession(args.model, providers=['CPUExecutionProvider'])
    random = np.random.default_rng(args.seed)
    metrics = defaultdict(list)
    env = UnityEnvironment(file_name=args.env, seed=args.seed, no_graphics=True,
                           worker_id=args.worker_id, side_channels=[engine, params, stats],
                           log_folder=str(Path(args.output).parent / 'unity-eval'))
    try:
        env.reset()
        behavior = next(iter(env.behavior_specs))
        for step in range(100000):
            decision, terminal = env.get_steps(behavior)
            n = len(decision)
            if n:
                assert decision.obs[0].shape == (n, 42), decision.obs[0].shape
                assert np.isfinite(decision.obs[0]).all(), 'Non-finite observation'
                assert np.abs(decision.obs[0]).max() <= 1.0001, 'Observation outside normalized range'
                masks = np.concatenate(decision.action_mask, axis=1)
                if model:
                    feed = {'obs_0': decision.obs[0], 'action_masks': (~masks).astype(np.float32)}
                    actions = model.run(['deterministic_discrete_actions'], feed)[0].astype(np.int32)
                else:
                    actions = np.empty((n, 2), dtype=np.int32)
                    for row in range(n):
                        for branch in range(2):
                            allowed = np.flatnonzero(~decision.action_mask[branch][row])
                            actions[row, branch] = random.choice(allowed)
                env.set_actions(behavior, ActionTuple(discrete=actions))
            env.step()
            for key, values in stats.get_and_reset_stats().items():
                metrics[key].extend(float(value) for value, _ in values)
            if len(metrics['Climb/Success']) >= args.episodes:
                break
        result = {'model': args.model or 'uniform random actions (untrained baseline)',
                  'seed': args.seed, 'level': args.level, 'stage': args.stage,
                  'episodes': len(metrics['Climb/Success']),
                  'means': {k: float(np.mean(v)) for k, v in metrics.items() if v},
                  'samples': dict(metrics)}
        Path(args.output).parent.mkdir(parents=True, exist_ok=True)
        Path(args.output).write_text(json.dumps(result, indent=2), encoding='utf-8')
        print(json.dumps({k: v for k, v in result.items() if k != 'samples'}, indent=2))
    finally:
        env.close()


if __name__ == '__main__':
    main()
