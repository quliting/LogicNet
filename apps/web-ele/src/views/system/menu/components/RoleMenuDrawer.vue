<script lang="ts" setup>
import type { BackendMenuItem, MenuTypeName } from '#/api/core/menu';

import { computed, nextTick, ref, watch } from 'vue';

import {
  ElAlert,
  ElButton,
  ElDrawer,
  ElEmpty,
  ElMessage,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTree,
} from 'element-plus';

import { getMenuTreeApi } from '#/api/core/menu';
import {
  getRoleListApi,
  getRoleMenusApi,
  saveRoleMenusApi,
  type RoleItem,
} from '#/api/core/role-menu';

defineOptions({ name: 'RoleMenuDrawer' });

const props = defineProps<{ modelValue: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [boolean] }>();

const visible = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value),
});

const typeTagType: Record<MenuTypeName, 'info' | 'success' | 'warning'> = {
  Directory: 'info',
  Menu: 'success',
  Button: 'warning',
};

const loading = ref(false);
const bindingLoading = ref(false);
const saving = ref(false);
const roles = ref<RoleItem[]>([]);
const menuTree = ref<BackendMenuItem[]>([]);
const selectedRoleId = ref('');
const treeRef = ref<InstanceType<typeof ElTree>>();

const selectedRole = computed(() =>
  roles.value.find((role) => role.Id === selectedRoleId.value),
);
const isAdmin = computed(() => selectedRole.value?.Name === 'Admin');
const canConfigure = computed(
  () => !!selectedRoleId.value && !isAdmin.value,
);

function getTypeTagType(type: MenuTypeName) {
  return typeTagType[type] ?? 'info';
}

function findPath(
  nodes: BackendMenuItem[],
  id: string,
  trail: BackendMenuItem[] = [],
): BackendMenuItem[] | null {
  for (const node of nodes) {
    const next = [...trail, node];
    if (node.Id === id) return next;
    if (node.Children?.length) {
      const found = findPath(node.Children, id, next);
      if (found) return found;
    }
  }
  return null;
}

function handleCheck(
  data: BackendMenuItem,
  info: { checkedKeys: Array<number | string> },
) {
  const id = String(data.Id);
  const checked = info.checkedKeys.some((key) => String(key) === id);

  // 与后端联动规则保持一致：
  // 勾选子级补全部祖先；取消父级时级联取消整棵子树。
  if (checked) {
    const path = findPath(menuTree.value, id) ?? [];
    path.slice(0, -1).forEach((node) => {
      treeRef.value?.setChecked(node.Id, true, false);
    });
    return;
  }

  const uncheckDescendants = (node: BackendMenuItem) => {
    treeRef.value?.setChecked(node.Id, false, false);
    node.Children?.forEach(uncheckDescendants);
  };
  data.Children?.forEach(uncheckDescendants);
}

function handleRoleClick(row?: null | RoleItem) {
  if (row) void selectRole(row);
}

async function selectRole(role: RoleItem) {
  selectedRoleId.value = role.Id;
  await nextTick();
  treeRef.value?.setCheckedKeys([], false);
  if (role.Name === 'Admin') return;

  bindingLoading.value = true;
  try {
    const menuIds = (await getRoleMenusApi(role.Id)) ?? [];
    const keys = menuIds.map((id) => String(id));
    await nextTick();
    treeRef.value?.setCheckedKeys(keys, false);
  } catch {
    // 失败提示由请求拦截器统一展示
  } finally {
    bindingLoading.value = false;
  }
}

async function loadBaseData() {
  loading.value = true;
  try {
    const [roleList, tree] = await Promise.all([
      getRoleListApi(),
      getMenuTreeApi(),
    ]);
    roles.value = roleList ?? [];
    menuTree.value = tree ?? [];
    const first = roles.value[0];
    if (first) await selectRole(first);
  } catch {
    // 失败提示由请求拦截器统一展示
  } finally {
    loading.value = false;
  }
}

async function handleSave() {
  if (!canConfigure.value) return;

  saving.value = true;
  try {
    const keys = (treeRef.value?.getCheckedKeys(false) ?? []).map((key) =>
      String(key),
    );
    await saveRoleMenusApi({
      RoleId: Number(selectedRoleId.value),
      MenuIds: keys.map((key) => Number(key)),
    });
    ElMessage.success('角色菜单授权已保存');
    // 保存后回读，展示后端补齐祖先后的最终绑定集合
    if (selectedRole.value) await selectRole(selectedRole.value);
  } catch {
    // 后端 Errors 文案由请求拦截器统一提示
  } finally {
    saving.value = false;
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) void loadBaseData();
  },
);
</script>

<template>
  <ElDrawer
    v-model="visible"
    title="角色菜单授权"
    size="860px"
    append-to-body
    destroy-on-close
  >
    <div v-loading="loading" class="flex h-full gap-4">
      <div class="flex w-56 shrink-0 flex-col">
        <div class="mb-2 text-sm text-gray-500">选择角色</div>
        <ElTable
          :data="roles"
          highlight-current-row
          height="100%"
          border
          @current-change="handleRoleClick"
        >
          <ElTableColumn label="角色" min-width="180">
            <template #default="{ row }">
              <div class="flex items-center justify-between gap-2">
                <span>{{ row.Name }}</span>
                <ElTag
                  v-if="row.Name === 'Admin'"
                  type="info"
                  size="small"
                  effect="plain"
                >
                  全量
                </ElTag>
              </div>
            </template>
          </ElTableColumn>
        </ElTable>
      </div>

      <div class="min-w-0 flex-1">
        <template v-if="selectedRole">
          <div class="mb-3 flex items-center justify-between">
            <div class="font-medium">
              当前角色：{{ selectedRole.Name }}
            </div>
            <ElButton
              v-if="canConfigure"
              type="primary"
              :loading="saving"
              @click="handleSave"
            >
              保存
            </ElButton>
          </div>

          <ElAlert
            v-if="isAdmin"
            type="info"
            :closable="false"
            show-icon
            title="Admin 角色始终可见全部菜单，无需绑定配置"
          />

          <div
            v-else
            v-loading="bindingLoading"
            class="h-[calc(100%-60px)] overflow-auto rounded border border-gray-200 p-3"
          >
            <ElEmpty v-if="!menuTree.length" description="暂无菜单数据" />
            <ElTree
              v-else
              ref="treeRef"
              :data="menuTree"
              node-key="Id"
              show-checkbox
              check-strictly
              default-expand-all
              :props="{ label: 'Name', children: 'Children' }"
              @check="handleCheck"
            >
              <template #default="{ data }">
                <span class="inline-flex items-center gap-2">
                  <span>{{ data.Name }}</span>
                  <ElTag :type="getTypeTagType(data.Type)" size="small">
                    {{ data.Type }}
                  </ElTag>
                </span>
              </template>
            </ElTree>
          </div>
        </template>
        <ElEmpty v-else description="请选择左侧角色" />
      </div>
    </div>
  </ElDrawer>
</template>
